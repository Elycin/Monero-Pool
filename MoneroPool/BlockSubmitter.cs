using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MoneroPool
{
    public class BlockSubmitter
    {
        public BlockSubmitter()
        {
            Logger.Log(Logger.LogLevel.Debug, "BlockSubmitter declared");
        }

        public async void Start()
        {
            Logger.Log(Logger.LogLevel.General, "Beginning Block Submittion thread!");

            await Task.Yield();
            while (true)
            {
                Thread.Sleep(5000);
                for (var i = 0; i < Statics.BlocksPendingSubmition.Count; i++)
                    try
                    {
                        var block = Statics.BlocksPendingSubmition[i];

                        if (!Statics.BlocksPendingPayment.Any(x => x.BlockHeight == block.BlockHeight))
                        {
                            Logger.Log(Logger.LogLevel.Special, "Submitting block with height {0}", block.BlockHeight);
                            var submitblock =
                                await
                                    Statics.DaemonJson.InvokeMethodAsync("submitblock",
                                        new JArray(
                                            BitConverter.ToString(block.BlockData)
                                                .Replace("-", "")));

                            try
                            {
                                if ((string) submitblock["result"]["status"] == "OK")
                                {
                                    Logger.Log(Logger.LogLevel.Special,
                                        "Block submitted was accepted! Adding for payment");
                                    var rBlock = Statics.RedisDb.Blocks.First(x => x.BlockHeight == block.BlockHeight);
                                    //
                                    rBlock.Found = true;
                                    rBlock.Founder = block.Founder;
                                    rBlock.FoundDateTime = DateTime.Now;

                                    Statics.RedisDb.SaveChanges(rBlock);

                                    var param = new JObject();
                                    param["height"] = block.BlockHeight;
                                    block.BlockHash =
                                        (string)
                                        (await
                                            Statics.DaemonJson.InvokeMethodAsync("getblockheaderbyheight",
                                                param))
                                        [
                                            "result"]["block_header"]["hash"];

                                    Statics.BlocksPendingPayment.Add(block);

                                    BackgroundStaticUpdater.ForceUpdate();
                                    //Force statics update to prevent creating orhpans ourselves, you don't want that now do you?
                                }
                                else
                                {
                                    Logger.Log(Logger.LogLevel.Error,
                                        "Block submittance failed with height {0} and error {1}!",
                                        block.BlockHeight, submitblock["result"]["status"]);
                                }
                            }
                            catch
                            {
                            }

                            Statics.BlocksPendingSubmition.RemoveAt(i);
                            i--;
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Log(Logger.LogLevel.Error, e.ToString());
                    }
            }
        }
    }
}