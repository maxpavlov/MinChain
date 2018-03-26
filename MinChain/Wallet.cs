using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using static MessagePack.MessagePackSerializer;

namespace MinChain
{
    public class Wallet
    {
        private readonly int currentIndex;
        readonly KeyPair keyPair;
        public ByteString Address { get; }

        public Wallet(KeyPair keyPair, int currentIndex){
            this.keyPair = keyPair;
            this.currentIndex = currentIndex;
            Address = ByteString.CopyFrom(keyPair.Address);

        }
        public Wallet(KeyPair keyPair):this(keyPair, 0){}

        //shouldn't have this method - debug only.
        private void dump()
        {
            var json = JsonConvert.SerializeObject(keyPair,
                    Formatting.Indented);
            Console.WriteLine(json);
        }

        /*
         * 
         * Next Private Key = hash( current public key + seed + currentIndex )
        */
        private Wallet next(byte[] seed)
        {
            byte[] currentIndexByte = BitConverter.GetBytes(currentIndex);

            byte[] beforehash = new byte[keyPair.PublicKey.Length + seed.Length + currentIndexByte.Length];

            System.Buffer.BlockCopy(keyPair.PublicKey, 0, beforehash, 0, keyPair.PublicKey.Length);
            System.Buffer.BlockCopy(seed, 0, beforehash, keyPair.PublicKey.Length, seed.Length);
            System.Buffer.BlockCopy(currentIndexByte, 0, beforehash, keyPair.PublicKey.Length + seed.Length, currentIndexByte.Length);

            byte[] nextPrivKey = Hash.ComputeDoubleSHA256(beforehash);

            byte[] nextPubKeyByte;

            EccService.GenerateKeyFromPrivateKey(nextPrivKey, out nextPubKeyByte);

            KeyPair nextKeyPair = new KeyPair
            {
                PrivateKey = nextPrivKey,
                PublicKey = nextPubKeyByte,
                Address = BlockchainUtil.ToAddress(nextPubKeyByte),
            };
            return new Wallet(nextKeyPair, currentIndex+1);
        }

        public ulong GetBalance(HashSet<TransactionOutput> utxos)
        {
            ulong sum = 0;
            foreach (var utxo in utxos)
            {
                if (!utxo.Recipient.Equals(Address)) continue;
                sum += utxo.Amount;
            }
            return sum;
        }

        public Transaction SendTo(
            HashSet<TransactionOutput> utxos,
            ByteString recipient, ulong amount)
        {
            // TODO: You should consider transaction fee.

            // Extract my spendable UTXOs.
            ulong sum = 0;
            var inEntries = new List<InEntry>();
            foreach (var utxo in utxos)
            {
                if (!utxo.Recipient.Equals(Address)) continue;
                inEntries.Add(new InEntry
                {
                    TransactionId = utxo.TransactionId,
                    OutEntryIndex = utxo.OutIndex,
                });

                sum += utxo.Amount;
                if (sum >= amount) goto CreateOutEntries;
            }

            throw new ArgumentException(
                "Insufficient fund.", nameof(amount));

        CreateOutEntries:
            // Create list of out entries.  It should contain fund transfer and
            // change if necessary.  Also the sum of outputs must be less than
            // that of inputs.  The difference will be collected as transaction
            // fee.
            var outEntries = new List<OutEntry>
            {
                new OutEntry
                {
                    RecipientHash = recipient,
                    Amount = amount,
                },
            };

            var change = sum - amount;
            if (change != 0)
            {
                outEntries.Add(new OutEntry
                {
                    RecipientHash = Address,
                    Amount = change,
                });
            }

            // Construct to-be-signed transaction.
            var transaction = new Transaction
            {
                Timestamp = DateTime.UtcNow,
                InEntries = inEntries,
                OutEntries = outEntries,
            };

            // Take a transaction signing hash and sign against it.  Since
            // wallet contains a single key pair, single signing is sufficient.
            var signHash = BlockchainUtil.GetTransactionSignHash(
                Serialize(transaction));
            var signature = EccService.Sign(
                signHash, keyPair.PrivateKey, keyPair.PublicKey);
            foreach (var inEntry in inEntries)
            {
                inEntry.PublicKey = keyPair.PublicKey;
                inEntry.Signature = signature;
            }

            var bytes = Serialize(transaction);
            return BlockchainUtil.DeserializeTransaction(bytes);
        }



        public class HierachicalWallet : Wallet
        {
            private readonly byte[] seed;
            public readonly List<Wallet> Wallets = new List<Wallet>();

            public HierachicalWallet(KeyPair keyPair) : base(keyPair)
            {
                this.seed = Hash.ComputeDoubleSHA256(keyPair.PrivateKey);

                Wallets.Add(this);
            }

            public void init(int numberOfWalletsToGenerate)
            {
                for (int i = 0; i < numberOfWalletsToGenerate; i++)
                {
                    var previousWallet = Wallets[i];
                    Wallets.Add(previousWallet.next(seed));
                }
            }

            public void dumpAll()
            {
                foreach (var w in Wallets)
                {
                    w.dump();
                }
            }

        }
    }
}