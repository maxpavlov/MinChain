using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static MessagePack.MessagePackSerializer;

namespace MinChain
{
    public partial class Runner
    {
        static readonly ILogger logger = Logging.Logger<Runner>();

        public static void Run(string[] args) =>
            new Runner().RunInternal(args);

        Configuration config;

        Wallet.HierachicalWallet hwallet;
        Block genesis;

        ConnectionManager connectionManager;
        InventoryManager inventoryManager;
        Executor executor;
        Mining miner;
        Storage storage;

        void RunInternal(string[] args)
        {
            if (!LoadConfiguration(args)) return;

            connectionManager = new ConnectionManager();
            inventoryManager = new InventoryManager();
            executor = new Executor();
            miner = new Mining();
            storage = new Storage(config.StoragePath, executor);

            connectionManager.NewConnectionEstablished += NewPeer;
            connectionManager.MessageReceived += HandleMessage;
            executor.BlockExecuted += miner.Notify;
            //executor.BlockExecuted += savfile;

            inventoryManager.ConnectionManager = connectionManager;
            inventoryManager.Executor = executor;

            executor.InventoryManager = inventoryManager;


            miner.ConnectionManager = connectionManager;
            miner.InventoryManager = inventoryManager;
            miner.Executor = executor;

            inventoryManager.Blocks.Add(genesis.Id, genesis.Original);
            executor.ProcessBlock(genesis);

            //process saved blocks

            foreach(var (id, block) in storage.LoadAll()){
                inventoryManager.TryLoadBlock(id, block);
            }


            //add save handler
            executor.BlockExecuted += storage.SaveBlock;
            //read all blocks

            connectionManager.Start(config.ListenOn);
            var t = Task.Run(async () =>
            {
                foreach (var ep in config.InitialEndpoints)
                    await connectionManager.ConnectToAsync(ep);
            });

            if (config.Mining)
            {
                miner.RecipientAddress = hwallet.Wallets[0].Address;
                miner.Start();
            }

            try
            {

                var web = new WebHostBuilder()
                    .UseKestrel()
                    .UseUrls("http://*:8881")
                    .Configure(app => app.Run(Handle))
                    .Build();

                web.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Port 8881 is in use. Web interface to poll the blockchain state is not going to be available for this node.");
            }


            Console.ReadLine();

            connectionManager.Dispose();
        }
        public async Task Handle(HttpContext request)
        {
            byte[] buf = null;

            if (request.Request.Path.ToString().Contains("latest"))
            {
                var json = JsonConvert.SerializeObject(executor.Latest, Formatting.Indented);

                buf = Encoding.ASCII.GetBytes(json);
            }
            //else if(request.Request.Path =="list"){
                
            //}

            await request.Response.Body.WriteAsync(buf, 0, buf.Length);
        }

        bool LoadConfiguration(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Should provide configuration file path.");
                return false;
            }

            try
            {
                config = JsonConvert.DeserializeObject<Configuration>(
                    File.ReadAllText(Path.GetFullPath(args[0])));
            }
            catch (Exception exp)
            {
                logger.LogError(
                    "Failed to load configuration file. Run 'config' command.",
                    exp);
                return false;
            }

            try
            {
                KeyPair seedKeyPair = KeyPair.LoadFrom(config.KeyPairPath);
                hwallet = new Wallet.HierachicalWallet(seedKeyPair);
                hwallet.init(10);
                hwallet.dumpAll();
                System.Threading.Thread.Sleep(3000);
            }
            catch (Exception exp)
            {
                logger.LogError(
                    $"Failed to load key from {config.KeyPairPath}.",
                    exp);
                return false;
            }
            try
            {
                var bytes = File.ReadAllBytes(config.GenesisPath);
                genesis = BlockchainUtil.DeserializeBlock(bytes);
            }
            catch (Exception exp)
            {
                logger.LogError(
                    $"Failed to load the genesis from {config.GenesisPath}.",
                    exp);
                return false;
            }

            return true;
        }

        // This enables to expand p2p network.
        //
        void NewPeer(int peerId)
        {
            var peers = connectionManager.GetPeers()
                .Select(x => x.ToString());
            connectionManager.SendAsync(new Hello
            {
                Genesis = genesis.Id,
                KnownBlocks = executor.Blocks.Keys.ToList(),
                MyPeers = peers.ToList(),
            }, peerId);
        }

        Task HandleMessage(Message message, int peerId)
        {
            switch (message.Type)
            {
                case MessageType.Hello:
                    return HandleHello(
                        Deserialize<Hello>(message.Payload),
                        peerId);

                case MessageType.Inventory:
                    return inventoryManager.HandleMessage(
                        Deserialize<InventoryMessage>(message.Payload),
                        peerId);

                default: return Task.CompletedTask;
            }
        }

        async Task HandleHello(Hello hello, int peerId)
        {
            // Check if the peer is on the same network.
            if (!genesis.Id.Equals(hello.Genesis))
                connectionManager.Close(peerId);

            var myBlocks = new HashSet<ByteString>();
            var peerBlocks = new HashSet<ByteString>();
            foreach (var blockId in executor.Blocks.Keys) myBlocks.Add(blockId);
            foreach (var blockId in hello.KnownBlocks) peerBlocks.Add(blockId);

            var messages = peerBlocks.Except(myBlocks)
                .Select(x => new InventoryMessage
                {
                    Type = InventoryMessageType.Request,
                    ObjectId = x,
                    IsBlock = true,
                })
                .ToArray();

            // Send request for unknown blocks.
            foreach (var message in messages)
                await connectionManager.SendAsync(message, peerId);
        }

        void savefile()
        {

        }
    }
}
