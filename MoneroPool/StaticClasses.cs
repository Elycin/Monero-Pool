using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using Mono.Net;
using Newtonsoft.Json.Linq;

namespace MoneroPool
{
    public static class Hash
    {
        public static byte[] CryptoNight(byte[] data)
        {
            var crytoNightHash = new byte[32];
            //Dirty hack for increased stack size
            var t = new Thread(
                () => NativeFunctions.cn_slow_hash(data, (uint) data.Length, crytoNightHash), 1024 * 1024 * 8);
            t.Start();
            t.Join();

            return crytoNightHash;
        }

        public static byte[] CryptoNightFastHash(byte[] data)
        {
            var crytoNightHash = new byte[32];
            //Dirty hack for increased stack size
            var t = new Thread(
                () => NativeFunctions.cn_fast_hash(data, (uint) data.Length, crytoNightHash), 1024 * 1024 * 8);
            t.Start();
            t.Join();

            return crytoNightHash;
        }
    }

    public enum ShareProcess
    {
        ValidShare,
        ValidBlock,
        InvalidShare
    }

    public enum StaticsLock
    {
        LockedByPool = 2,
        LockedByBackGroundUpdater = 1,
        NoLock = 0
    }

    public class PoolHashRateCalculation
    {
        public DateTime Begin;
        public uint Difficulty;
        public ulong Time;
    }

    public static class Helpers
    {
        public static double GetHashRate(List<uint> difficulty, ulong time) =>
            GetHashRate(difficulty.Sum(x => (double) x), time);

        public static double GetHashRate(double difficulty, ulong time)
        {
            //Thanks surfer43    , seriously thank you, It works great

            Logger.Log(Logger.LogLevel.Debug, "Returning hash rate of {0}", difficulty / time);
            return difficulty / time;
        }

        public static double GetWorkerHashRate(ConnectedWorker worker)
        {
            var time =
                (ulong)
                (worker.ShareDifficulty.Skip(worker.ShareDifficulty.Count - 4).First().Key -
                 worker.ShareDifficulty.Last().Key).Seconds;
            return GetHashRate(
                worker.ShareDifficulty.Skip(worker.ShareDifficulty.Count - 4)
                    .ToDictionary(x => x.Key, x => (uint) x.Value)
                    .Values.ToList(), time);
        }

        public static double GetMinerWorkerHashRate(MinerWorker worker)
        {
            //don't covnert to dictionary, rare but as seen in testing time stamps may be same
            double time = 0;
            double difficulty = 0;
            foreach (var shareDifficulty in worker.ShareDifficulty)
            {
                time += shareDifficulty.Key.TotalSeconds;
                difficulty += shareDifficulty.Value;
            }

            return GetHashRate(difficulty, (ulong) time);
        }

        public static double GetMinerHashRate(Miner worker)
        {
            double hashRate = 0;
            worker.MinersWorker.ForEach(x =>
                hashRate += Program.RedisPoolDatabase.MinerWorkers.First(x2 => x2.Identifier == x).HashRate);
            return hashRate;
        }


        public static uint WorkerVardiffDifficulty(ConnectedWorker worker)
        {
            double aTargetTime = int.Parse(Program.Configuration.IniReadValue("vardiff-targettime-seconds"));

            uint returnValue = 0;
            // Don't keep it no zone forever
            if ((DateTime.Now - worker.LastShare).TotalSeconds > aTargetTime)
            {
                var deviance = 100 - (DateTime.Now - worker.LastShare).Seconds * 100 / aTargetTime;
                if (Math.Abs(deviance) > int.Parse(Program.Configuration.GetMaxDeviation()))
                    deviance = -int.Parse(Program.Configuration.GetMaxDeviation());
                returnValue = (uint) (worker.LastDifficulty * (100 + deviance) / 100);
            }
            else
            {
                //We calculate average of last 4 shares.

                var aTime = worker.ShareDifficulty.Skip(worker.ShareDifficulty.Count - 4).Take(4)
                                .Sum(x => x.Key.TotalSeconds) / 4;


                var deviance = 100 -
                               aTime * 100 / int.Parse(Program.Configuration.GetShareTargetTime());

                if (Math.Abs(deviance) < int.Parse(Program.Configuration.GetStartingDeviation("vardiff-targettime-deviation-allowed")))
                {
                    returnValue = worker.LastDifficulty;
                }
                else if (deviance > 0)
                {
                    if (deviance > int.Parse(Program.Configuration.IniReadValue("vardiff-targettime-maxdeviation")))
                        deviance = int.Parse(Program.Configuration.IniReadValue("vardiff-targettime-maxdeviation"));
                    returnValue = (uint) (worker.LastDifficulty * (100 + deviance) / 100);
                }
                else
                {
                    if (Math.Abs(deviance) > int.Parse(Program.Configuration.IniReadValue("vardiff-targettime-maxdeviation")))
                        deviance = -int.Parse(Program.Configuration.IniReadValue("vardiff-targettime-maxdeviation"));
                    returnValue = (uint) (worker.LastDifficulty * (100 + deviance) / 100);
                }
            }

            if (returnValue < uint.Parse(Program.Configuration.IniReadValue("base-difficulty")))
                returnValue = uint.Parse(Program.Configuration.IniReadValue("base-difficulty"));
            else if (returnValue > uint.Parse(Program.Configuration.IniReadValue("vardiff-max-difficulty")))
                returnValue = uint.Parse(Program.Configuration.IniReadValue("vardiff-max-difficulty"));
            Logger.Log(Logger.LogLevel.Debug, "Returning new difficulty if {0} vs previous {1}", returnValue,
                worker.LastDifficulty);
            return returnValue;
        }

        public static string GenerateUniqueWork(ref int seed)
        {
            seed = Statics.ReserveSeed++;
            var work = StringToByteArray((string) Statics.CurrentBlockTemplate["blocktemplate_blob"]);

            Array.Copy(BitConverter.GetBytes(seed), 0, work, (int) Statics.CurrentBlockTemplate["reserved_offset"], 4);

            work = GetConvertedBlob(work);

            Logger.Log(Logger.LogLevel.Debug, "Generated unqiue work for seed {0}", seed);
            return BitConverter.ToString(work).Replace("-", "");
        }

        public static byte[] GenerateShareWork(int seed, bool convert)
        {
            var work = StringToByteArray((string) Statics.CurrentBlockTemplate["blocktemplate_blob"]);

            Array.Copy(BitConverter.GetBytes(seed), 0, work, (int) Statics.CurrentBlockTemplate["reserved_offset"], 4);

            if (convert)
                work = GetConvertedBlob(work);

            Logger.Log(Logger.LogLevel.Debug, "Generated share work for seed {0}", seed);

            return work;
        }

        public static uint SwapEndianness(uint x)
        {
            return ((x & 0x000000ff) << 24) + // First byte
                   ((x & 0x0000ff00) << 8) + // Second byte
                   ((x & 0x00ff0000) >> 8) + // Third byte
                   ((x & 0xff000000) >> 24); // Fourth byte
        }

        public static uint GetTargetFromDifficulty(uint difficulty)
        {
            return uint.MaxValue / difficulty;
        }

        public static string GetRequestBody(HttpListenerRequest request)
        {
            //disposale messes up mono

            string documentContents;
            var readStream = new StreamReader(request.InputStream, Encoding.UTF8);

            documentContents = readStream.ReadToEnd();

            //readStream.Dispose();

            return documentContents;
        }

        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                .ToArray();
        }

        public static ShareProcess ProcessShare(byte[] blockHash, int blockDifficulty, uint shareDifficulty)
        {
            var diff = new BigInteger(
                StringToByteArray("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00"));

            var blockList = blockHash.ToList();
            blockList.Add(0x00);
            var block = new BigInteger(blockList.ToArray());

            var blockDiff = diff / block;
            if (blockDiff >= blockDifficulty)
            {
                Logger.Log(Logger.LogLevel.General, "Block found with hash:{0}",
                    BitConverter.ToString(blockHash).Replace("-", ""));
                return ShareProcess.ValidBlock;
            }

            if (blockDiff < shareDifficulty)
            {
                Logger.Log(Logger.LogLevel.General, "Invalid share found with hash:{0}",
                    BitConverter.ToString(blockHash).Replace("-", ""));
                return ShareProcess.InvalidShare;
            }

            Logger.Log(Logger.LogLevel.General, "Valid share found with hash:{0}",
                BitConverter.ToString(blockHash).Replace("-", ""));
            return ShareProcess.ValidShare;
        }

        public static bool IsValidAddress(string address, uint prefix)
        {
            var ret = NativeFunctions.check_account_address(address, prefix);
            if (ret == 0)
                return false;
            return true;
        }

        public static byte[] GetConvertedBlob(byte[] blob)
        {
            var converted = new byte[128];
            var returnLength = NativeFunctions.convert_block(blob, blob.Length, converted);
            return converted.Take((int) returnLength).ToArray();
        }
    }
}