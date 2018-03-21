using System;
using System.Linq;
using System.Security.Cryptography;
using static System.Console;


namespace MinChain
{
    public static class Hash
    {
        public static byte[] ComputeSHA256(byte[] bytes)
        {
            using (var sha256 = SHA256.Create())
                return sha256.ComputeHash(bytes);
        }

        //typicall they use doublehash to be defensive against birthday attack.
        public static byte[] ComputeDoubleSHA256(byte[] bytes)
        {
            using (var sha256 = SHA256.Create())
                return sha256.ComputeHash(sha256.ComputeHash(bytes));
        }

        public static double Difficulty(byte[] hash)
        {
            var bytes = new byte[] { 0x3F, 0xF0 }.Concat(hash).Take(8);
            if (BitConverter.IsLittleEndian) bytes = bytes.Reverse();

            var d = BitConverter.ToDouble(bytes.ToArray(), 0);
            return Math.Pow(2, -35) / (d - 1);
        }

        //public static void Main(string[] args){
        //    byte[] b = HexConvert.ToBytes("0000123");
        //    WriteLine(Difficulty(b));
        //}
    }
}
