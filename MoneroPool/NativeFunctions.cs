using System;
using System.Runtime.InteropServices;

namespace MoneroPool
{
    public static class NativeFunctions
    {
        public static bool IsLinux
        {
            get
            {
                var p = (int) Environment.OSVersion.Platform;
                return p == 4 || p == 6 || p == 128;
            }
        }

        [DllImport("CryptoNight", EntryPoint = "cn_slow_hash")]
        public static extern void cn_slow_hash(byte[] data, uint length, byte[] hash);

        [DllImport("CryptoNight", EntryPoint = "cn_fast_hash")]
        public static extern void cn_fast_hash(byte[] data, uint length, byte[] hash);

        [DllImport("CryptoNight", EntryPoint = "check_account_address")]
        public static extern uint check_account_address(string address, uint prefix);

        [DllImport("CryptoNight", EntryPoint = "convert_block")]
        public static extern uint convert_block(byte[] cblock, int length, byte[] convertedblock);
    }
}