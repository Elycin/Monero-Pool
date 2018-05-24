﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MoneroPool
{
    public class BlockPayment
    {
        public BlockPayment()
        {
            Logger.Log(Logger.LogLevel.Debug, "BlockPayment declared");
        }


        private static async void InitiatePayments(ulong reward, PoolBlock block)
        {
            var sharePerAddress = new Dictionary<string, double>();
            var lastPaidBlock = 0;

            double totalShares = 0;

            try
            {
                lastPaidBlock = Statics.RedisDb.Information.LastPaidBlock;
            }
            catch
            {
                lastPaidBlock = 0;
            }


            foreach (var miner in Statics.RedisDb.Miners)
            {
                sharePerAddress.Add(miner.Address, 0);
                double shares = 0;

                var blocks =
                    Statics.RedisDb.Blocks.Where(
                        x => x.BlockHeight > lastPaidBlock && x.BlockHeight <= block.BlockHeight).ToArray();

                foreach (
                    var blockReward in
                    Statics.RedisDb.BlockRewards.Where(
                        x =>
                            x.Miner == miner.Identifier &&
                            blocks.Any(x2 => x2.Identifier == x.Block)))
                foreach (var share in Statics.RedisDb.Shares.Where(x => blockReward.Shares.Contains(x.Identifier)))
                {
                    shares += share.Value;
                    totalShares += share.Value;
                }

                sharePerAddress[miner.Address] = shares;
            }

            var fee = 100 + int.Parse(Statics.Config.IniReadValue("pool-fee"));

            var rewardPerShare = reward / ((100 + fee) * totalShares / 100);

            var param = new JObject();

            var destinations = new JArray();

            foreach (var addressShare in sharePerAddress)
            {
                var destination = new JObject();
                destination["amount"] = (long) (addressShare.Value * rewardPerShare);
                destination["address"] = addressShare.Key;
                var miner = Statics.RedisDb.Miners.First(x => x.Address == addressShare.Key);
                miner.TotalPaidOut += (ulong) (addressShare.Value * rewardPerShare);
                Statics.RedisDb.SaveChanges(miner);
                destinations.Add(destination);
            }

            param["destinations"] = destinations;

            param["fee"] = ulong.Parse(Statics.Config.IniReadValue("tx-fee"));
            param["mixin"] = 0;
            param["unlock_time"] = 0;

            var transfer = Statics.WalletJson.InvokeMethod("transfer", param);

            Statics.RedisDb.Information.LastPaidBlock = block.BlockHeight;
            Statics.RedisDb.SaveChanges(Statics.RedisDb.Information);

            Console.WriteLine(transfer);
        }

        public async void Start()
        {
            await Task.Yield();
            Logger.Log(Logger.LogLevel.General, "Beginning Block Payment thread!");

            while (true)
            {
                Thread.Sleep(5000);
                for (var i = 0; i < Statics.BlocksPendingPayment.Count; i++)
                {
                    var pBlock = Statics.BlocksPendingPayment[i];
                    var hash = pBlock.BlockHash;
                    var param = new JObject();
                    param["hash"] = hash;
                    var block =
                        (JObject) (await Statics.DaemonJson.InvokeMethodAsync("getblockheaderbyhash", param))["result"][
                            "block_header"];
                    var confirms = (int) block["depth"];
                    if (!(bool) block["orphan_status"] &&
                        confirms >= int.Parse(Statics.Config.IniReadValue("block-confirms")))
                    {
                        //Do payments
                        Logger.Log(Logger.LogLevel.Special, "Initiating payments for block with height {0}",
                            pBlock.BlockHeight);
                        InitiatePayments((ulong) block["reward"], pBlock);

                        Statics.BlocksPendingPayment.RemoveAt(i);
                        i--;
                    }

                    if ((bool) block["orphan_status"])
                    {
                        //Orphaned      
                        Logger.Log(Logger.LogLevel.Error, "Block with hieght {0}  orphaned!", pBlock.BlockHeight);
                        var rBlock = Statics.RedisDb.Blocks.First(x => x.BlockHeight == pBlock.BlockHeight);
                        rBlock.Orphan = true;
                        Statics.RedisDb.SaveChanges(rBlock);
                        Statics.BlocksPendingPayment.RemoveAt(i);
                        i--;
                    }
                }
            }
        }
    }
}