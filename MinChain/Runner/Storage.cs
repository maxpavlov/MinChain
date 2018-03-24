using System;
using System.Collections.Generic;
using System.IO;

namespace MinChain
{
    public class Storage
    {
        private string storagePath;
        private Executor executor;
        public Storage(string path, Executor executor)
        {
            storagePath = path;
            this.executor = executor;
        }

        public IEnumerable<(ByteString, byte[])> LoadAll(){

            foreach(var file in Directory.GetFiles(storagePath)){

                var bytes = File.ReadAllBytes(file);
                var block = BlockchainUtil.DeserializeBlock(bytes);

                yield return (block.Id, bytes);
            }
        }

        public void SaveBlock(){
            var block = executor.Latest;
            using (var fs = File.OpenWrite(System.IO.Path.Combine(storagePath,  block.Id.ToString() )))
            {
                fs.Write(block.Original, 0, block.Original.Length);
                fs.Flush();
            }
        }
    }
}
