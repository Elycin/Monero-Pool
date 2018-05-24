using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MoneroPool
{
    public class BackgroundStaticUpdater
    {
        public BackgroundStaticUpdater()
        {
            Logger.Log(Logger.LogLevel.Debug, "BackgroundStaticUpdater declared");
        }

        public static void ForceUpdate()
        {
            Logger.Log(Logger.LogLevel.Verbose, "Background updater forced!");
            Program.CurrentBlockHeight = (int) Statics.DaemonJson.InvokeMethod("getblockcount")["result"]["count"];
            Program.CurrentBlockTemplate = (JObject)
                Program.DaemonJson.InvokeMethod("getblocktemplate",
                        new JObject(
                            new JProperty(
                                "reserve_size", 4),
                            new JProperty(
                                "wallet_address",
                                Statics.Config
                                    .IniReadValue(
                                        "wallet-address"))))
                    ["result"];
        }

        public async void Start()
        {
            Logger.Log(Logger.LogLevel.General, "Beginning Background Updater thread!");

            await Task.Yield();
            Program.CurrentBlockHeight =
                (int) (await Program.DaemonJson.InvokeMethodAsync("getblockcount"))["result"]["count"];
            Program.CurrentBlockTemplate = (JObject)
                (await
                    Program.DaemonJson.InvokeMethodAsync("getblocktemplate",
                        new JObject(
                            new JProperty(
                                "reserve_size", 4),
                            new JProperty(
                                "wallet_address",
                                Program.Config
                                    .IniReadValue(
                                        "wallet-address")))))["result"];
            if (Program.CurrentBlockTemplate == null || Statics.CurrentBlockHeight == 0)
            {
                Logger.Log(Logger.LogLevel.Error, "Failed to get block template and height. Shutting down!");
                Environment.Exit(-1);
            }

            Program.HashRate.Begin = DateTime.Now;
            Program.HashRate.Difficulty = 0;
            Logger.Log(Logger.LogLevel.General, "Acquired block template and height, miners can connet now!");
            while (true)
                try
                {
                    var newBlockHeight =
                        (int) (await Program.DaemonJson.InvokeMethodAsync("getblockcount"))["result"]["count"];
                    Logger.Log(Logger.LogLevel.General, "Current pool hashrate : {0} Hashes/Second",
                        Helpers.GetHashRate(Program.HashRate.Difficulty, Program.HashRate.Time));

                    Program.RedisPoolDatabase.Information.CurrentBlock = newBlockHeight;
                    Program.RedisPoolDatabase.Information.NewtworkHashRate =
                        (double) (int) Program.CurrentBlockTemplate["difficulty"] / 60;
                    Program.RedisPoolDatabase.Information.PoolHashRate = Helpers.GetHashRate(
                        Program.HashRate.Difficulty,
                        Program.HashRate.Time);
                    Program.RedisPoolDatabase.Information.SharesPerSecond = (double) Program.TotalShares / 5;
                    Program.RedisPoolDatabase.Information.RoundShares += Program.TotalShares;
                    Program.RedisPoolDatabase.Information.BaseDificulty =
                        int.Parse(Program.confi.IniReadValue("base-difficulty"));
                    Program.TotalShares = 0;
                    Program.RedisPoolDatabase.SaveChanges(Program.RedisPoolDatabase.Information);

                    Logger.Log(Logger.LogLevel.Verbose, "Updated database!");
                    if (newBlockHeight != Statics.CurrentBlockHeight)
                    {
                        Statics.CurrentBlockTemplate = (JObject)
                            (await
                                Statics.DaemonJson.InvokeMethodAsync("getblocktemplate",
                                    new JObject(
                                        new JProperty(
                                            "reserve_size", 4),
                                        new JProperty(
                                            "wallet_address",
                                            Statics.Config
                                                .IniReadValue
                                                (
                                                    "wallet-address")))))
                            ["result"];

                        Statics.HashRate.Difficulty = 0;
                        Statics.HashRate.Time = 0;
                        Statics.HashRate.Begin = DateTime.Now;
                        Statics.RedisDb.Information.RoundShares = 0;
                        Statics.RedisDb.SaveChanges(Statics.RedisDb.Information);
                        Statics.CurrentBlockHeight = newBlockHeight;
                        Logger.Log(Logger.LogLevel.General, "New block with height {0}, updating Tcp Miners",
                            newBlockHeight);

                        var localCopy = Statics.ConnectedClients.ToDictionary(x => x.Key, x => x.Value);

                        foreach (var connectedClient in localCopy)
                            if (connectedClient.Value.TcpClient != null)
                            {
                                var response = new JObject();

                                response["jsonrpc"] = "2.0";

                                response["method"] = "job";

                                var bluffResponse = new JObject();
                                CryptoNightPool.GenerateGetJobResponse(ref bluffResponse, connectedClient.Key);

                                response["params"] = bluffResponse["result"];
                                var s = JsonConvert.SerializeObject(response);
                                s += "\n";
                                var byteArray = Encoding.UTF8.GetBytes(s);
                                connectedClient.Value.TcpClient.GetStream().Write(byteArray, 0, byteArray.Length);
                            }
                    }

                    var list = Statics.ConnectedClients.ToList();

                    Logger.Log(Logger.LogLevel.Debug, "Beginning client validation", newBlockHeight);

                    Parallel.For(0, list.Count, i =>
                    {
                        try
                        {
                            var miner = Statics.RedisDb.Miners.First(x => x.Address == list[i].Value.Address);
                            if (miner.TimeHashRate == null)
                            {
                                miner.TimeHashRate = new Dictionary<DateTime, double>();
                                miner.TimeHashRate.Add(DateTime.Now, Helpers.GetMinerHashRate(miner));
                            }

                            if ((DateTime.Now - list[i].Value.LastSeen).TotalSeconds >
                                int.Parse(Statics.Config.IniReadValue("client-timeout-seconds")))
                            {
                                Logger.Log(Logger.LogLevel.General, "Removing time out client {0}", list[i].Key);
                                Statics.ConnectedClients.Remove(list[i].Key);

                                miner.MinersWorker.Remove(list[i].Key);
                                miner.TimeHashRate.Add(DateTime.Now, Helpers.GetMinerHashRate(miner));

                                Statics.RedisDb.Remove(
                                    Statics.RedisDb.MinerWorkers.First(x => x.Identifier == list[i].Key));
                            }
                            else if ((DateTime.Now - miner.TimeHashRate.Last().Key).Minutes > 5)
                            {
                                miner.TimeHashRate.Add(DateTime.Now, Helpers.GetMinerHashRate(miner));
                            }

                            foreach (
                                var cur in miner.TimeHashRate.Where(x => (DateTime.Now - x.Key).Days >= 1).ToList())
                                miner.TimeHashRate.Remove(cur.Key);
                            Statics.RedisDb.SaveChanges(miner);
                        }
                        catch
                        {
                        }
                    });
                    Thread.Sleep(5000);
                }
                catch (Exception e)
                {
                    Logger.Log(Logger.LogLevel.Error, e.ToString());
                }
        }
    }
}