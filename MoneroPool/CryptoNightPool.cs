using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using HttpListener = Mono.Net.HttpListener;
using HttpListenerContext = Mono.Net.HttpListenerContext;

namespace MoneroPool
{
    public class CryptoNightPool
    {
        public CryptoNightPool()
        {
            Logger.Log(Logger.LogLevel.Debug, "CryptoNightPool declared");
        }

        public async Task HttpServer()
        {
            var listener = new HttpListener();
            listener.Prefixes.Add(Program.Configuration.IniReadValue("http-server"));
            listener.Start();
            while (true)
            {
                var client = await listener.GetContextAsync();
                if (Program.RedisPoolDatabase.Bans.Any(x => x.IpBan == client.Request.UserHostAddress))
                {
                    var ban = Program.RedisPoolDatabase.Bans.First(x => x.IpBan == client.Request.UserHostAddress);
                    if ((DateTime.Now - ban.Begin).Minutes > ban.Minutes)
                    {
                        AcceptHttpClient(client);
                        Program.RedisPoolDatabase.Remove(ban);
                    }
                    else
                    {
                        Logger.Log(Logger.LogLevel.General, "Reject ban client from ip {0}",
                            client.Request.UserHostAddress);
                    }
                }
                else
                {
                    AcceptHttpClient(client);
                }
            }
        }

        /// <summary>
        ///  Start the pool socket server.
        /// </summary>
        /// <returns></returns>
        public async Task TcpServer()
        {
            // Start the socket server.
            var listener = new TcpListener(IPAddress.Any, Program.Configuration.GetStratumPort());
            listener.Start();

            // Continious loop
            while (true)
            {
                // Asyncronously accept connections.
                var connectedMiner = await listener.AcceptTcpClientAsync();

                // Check if banned.
                if (Program.RedisPoolDatabase.Bans.Any(x => x.IpBan == connectedMiner.Client.RemoteEndPoint.Address)
                {
                    // The user is banned - Get the information about the ban.
                    var ban = Program.RedisPoolDatabase.Bans.First(x =>
                        x.IpBan == ((IPEndPoint) connectedMiner.Client.RemoteEndPoint).Address.ToString());

                    // Compare the ban time to see if 
                    if ((DateTime.Now - ban.Begin).Minutes > ban.Minutes)
                    {
                        AcceptTcpClient(connectedMiner);
                        Program.RedisPoolDatabase.Remove(ban);
                    }
                    else
                    {
                        Logger.Log(Logger.LogLevel.General, "Banned address attempted to connect: {0}",
                            connectedMiner.Client.RemoteEndPoint);
                    }
                }
                else
                {
                    // No ban detected.
                    AcceptTcpClient(connectedMiner);
                }
            }
        }

        public async void Start()
        {
            Logger.Log(Logger.LogLevel.General, "Beginning Listen!");

            HttpServer();

            TcpServer();
        }

        public void IncreaseShareCount(string guid, uint difficulty)
        {
            var currentWorker = Program.ConnectedClients[guid];
            var shareValue = difficulty / Program.Configuration.GetBaseDifficulty();

            // Get the current block height.
            Block block = Program.RedisPoolDatabase.Blocks.Any(x => x.BlockHeight == Program.CurrentBlockHeight)
                ? Program.RedisPoolDatabase.Blocks.First(x => x.BlockHeight == Program.CurrentBlockHeight)
                : new Block(Program.CurrentBlockHeight);

            // Get the current miner's addrerss.
            Miner miner = Program.RedisPoolDatabase.Miners.Any(x => x.Address == currentWorker.Address)
                ? Program.RedisPoolDatabase.Miners.First(x => x.Address == currentWorker.Address)
                : new Miner(currentWorker.Address, 0);

            // Iterate through each block reward.
            foreach (var fBlockReward in Program.RedisPoolDatabase.BlockRewards)
            {
                // Check if the current block.
                if (fBlockReward.Block == block.Identifier && fBlockReward.Miner == miner.Identifier)
                {
                    var fShare = new Share(fBlockReward.Identifier, shareValue);
                    fBlockReward.Shares.Add(fShare.Identifier);

                    // Save changes to the redis database.
                    Program.RedisPoolDatabase.SaveChanges(fBlockReward);
                    Program.RedisPoolDatabase.SaveChanges(fShare);
                    Program.RedisPoolDatabase.SaveChanges(miner);
                    Program.RedisPoolDatabase.SaveChanges(block);

                    // Return so we stop burning cycles.
                    return;
                }
            }

            // Get the block reward and current share.
            var blockReward = new BlockReward(miner.Identifier, block.Identifier);
            var share = new Share(blockReward.Identifier, shareValue);

            // Track the data in proper lists.
            blockReward.Shares.Add(share.Identifier);
            miner.BlockReward.Add(blockReward.Identifier);
            block.BlockRewards.Add(blockReward.Identifier);

            // Save all data to the redis database.
            Program.RedisPoolDatabase.SaveChanges(blockReward);
            Program.RedisPoolDatabase.SaveChanges(share);
            Program.RedisPoolDatabase.SaveChanges(miner);
            Program.RedisPoolDatabase.SaveChanges(block);
        }

        public bool GenerateSubmitResponse(ref JObject response, string jobId, string guid, byte[] nonce,
            string resultHash, string ipAddress)
        {
            var worker = Statics.ConnectedClients[guid];

            Program.TotalShares++;
            var result = new JObject();

            if (nonce == null || nonce.Length == 0)
            {
                response["error"] = "Invalid arguments!";
                return false;
            }

            try
            {
                var shareJob = worker.JobSeed.First(x => x.Key == jobId).Value;
                if (!shareJob.SubmittedShares.Contains(BitConverter.ToInt32(nonce, 0)))
                {
                    shareJob.SubmittedShares.Add(BitConverter.ToInt32(nonce, 0));
                }
                else
                {
                    response["error"] = "Duplicate share";
                    return false;
                }

                var jobSeed = shareJob.Seed;
                worker.ShareRequest(shareJob.CurrentDifficulty);
                Program.RedisPoolDatabase.MinerWorkers.First(x => x.Identifier == guid)
                    .ShareRequest(shareJob.CurrentDifficulty);
                var prevJobBlock = Helpers.GenerateShareWork(jobSeed, true);

                Array.Copy(nonce, 0, prevJobBlock, 39, nonce.Length);
                var blockHash = Hash.CryptoNight(prevJobBlock);

                Program.ConnectedClients[guid].LastSeen = DateTime.Now;

                if (resultHash.ToUpper() != BitConverter.ToString(blockHash).Replace("-", ""))
                {
                    Logger.Log(Logger.LogLevel.General, "Hash mismatch from {0}", guid);

                    result["status"] = "Hash mismatch ";
                    //    throw new Exception();
                }
                else
                {
                    var shareProcess = Helpers.ProcessShare(
                        blockHash,
                        (int) Program.CurrentBlockTemplate["difficulty"],
                        (uint) shareJob.CurrentDifficulty
                    );

                    // Get the current address for the miner.
                    var address = Program.ConnectedClients[guid].Address;

                    worker.TotalShares++;

                    if (shareProcess == ShareProcess.ValidShare || shareProcess == ShareProcess.ValidBlock)
                    {
                        // Increment the difficulty by the current share.
                        Program.HashRate.Difficulty += (uint) shareJob.CurrentDifficulty;

                        // Incrememtn the time by the epoch.
                        Program.HashRate.Time = (ulong) (DateTime.Now - Program.HashRate.Begin).TotalSeconds;

                        // Try to increase the share count.
                        try
                        {
                            IncreaseShareCount(guid, (uint) shareJob.CurrentDifficulty);
                        }
                        catch (Exception e)
                        {
                            Logger.Log(Logger.LogLevel.Error, e.ToString());
                            throw;
                        }

                        // Check to see if we've found a block.
                        if (shareProcess == ShareProcess.ValidBlock &&
                            !Program.BlocksPendingSubmition.Any(x => x.BlockHeight == worker.CurrentBlock))
                        {
                            Logger.Log(Logger.LogLevel.Special, "Block found by {0}", guid);
                            var shareWork = Helpers.GenerateShareWork(jobSeed, false);
                            Array.Copy(nonce, 0, shareWork, 39, nonce.Length);

                            Program.BlocksPendingSubmition.Add(new PoolBlock(shareWork, worker.CurrentBlock, "",
                                Program.ConnectedClients[guid].Address));
                        }

                        // Set a status
                        result["status"] = "OK";
                    }
                    else
                    {
                        // Mark share as failed.
                        result["status"] = "Invalid share.";

                        // Keep track of the failed share count.
                        worker.RejectedShares++;

                        // Check to see if we should ban.
                        if ((double) worker.RejectedShares / worker.TotalShares >
                            int.Parse(Program.Configuration.IniReadValue("ban-reject-percentage")) &&
                            worker.TotalShares > int.Parse(Program.Configuration.IniReadValue("ban-after-shares")))
                        {
                            // we're going to ban the miner.
                            result["status"] = "Address banned for high rejected share rate.";

                            // Get the interval of which the miner will be banned.
                            var minutes = int.Parse(Program.Configuration.IniReadValue("ban-time-minutes"));

                            // Log to the console that the address was banned.
                            Logger.Log(Logger.LogLevel.General,
                                "Client {0} ip banned for {1} minutes for having a reject rate of {2}",
                                ipAddress,
                                minutes,
                                (worker.RejectedShares / worker.TotalShares)
                            );


                            // Append the ban to the database
                            Program.RedisPoolDatabase.SaveChanges(
                                new Ban
                                {
                                    AddressBan = worker.Address,
                                    IpBan = ipAddress,
                                    Begin = DateTime.Now,
                                    Minutes = minutes
                                }
                            );

                            // Return the result to the packet.
                            response["result"] = result;
                            return true;
                        }
                    }
                }
            }
            catch
            {
                result["error"] = "Invalid job id";
            }

            response["result"] = result;

            return false;
        }

        /// <summary>
        /// Build the job assignment packet.
        /// </summary>
        /// <param name="response"></param>
        /// <param name="guid"></param>
        public static void GenerateGetJobResponse(ref JObject response, string guid)
        {
            var job = new JObject();

            var worker = Statics.ConnectedClients.First(x => x.Key == guid).Value;
            worker.LastSeen = DateTime.Now;
            /*if (worker.ShareDifficulty.Count >= 4)
                worker.LastDifficulty = Helpers.WorkerVardiffDifficulty(worker);  */


            Logger.Log(Logger.LogLevel.General, "Getwork request from {0}", guid);

            //result["id"] = guid;

            var seed = 0;
            if (worker.PendingDifficulty != worker.LastDifficulty || worker.CurrentBlock != Statics.CurrentBlockHeight)
            {
                // Tell the worker the current block height
                worker.CurrentBlock = Program.CurrentBlockHeight;
                worker.LastDifficulty = worker.PendingDifficulty;
                job["blob"] = Helpers.GenerateUniqueWork(ref seed);

                // Assign the job 
                job["job_id"] = Guid.NewGuid().ToString();
                var shareJob = new ShareJob();
                shareJob.CurrentDifficulty = worker.LastDifficulty;
                shareJob.Seed = seed;
                worker.JobSeed.Add(new KeyValuePair<string, ShareJob>((string) job["job_id"], shareJob));


                // Assign the miner the number of concurrent jobs.
                if (worker.JobSeed.Count > int.Parse(Program.Configuration.IniReadValue("max-concurrent-works")))
                {
                    worker.JobSeed.RemoveAt(0);
                }   

                // Add the tareget difficulty.
                job["target"] = BitConverter.ToString(
                    BitConverter.GetBytes(
                        Helpers.GetTargetFromDifficulty((uint) shareJob.CurrentDifficulty)
                    )
                ).Replace("-", "");
            }
            else
            {
                // Send empty data.
                job["blob"] = "";
                job["job_id"] = "";
                job["target"] = "";
            }

            // Assign the job signal to the result.
            response["result"] = job;

            // Get the first worker
            var minerWorker = Program.RedisPoolDatabase.MinerWorkers.First(x => x.Identifier == guid);
            minerWorker.NewJobRequest();
            Program.RedisPoolDatabase.SaveChanges(minerWorker);
            Program.ConnectedClients[guid] = worker;

            Logger.Log(Logger.LogLevel.Verbose, "Finsihed job response");
        }

        public void GenerateLoginResponse(ref JObject response, string guid, string address)
        {
            var result = new JObject();
            var job = new JObject();

            if (!Helpers.IsValidAddress(address, uint.Parse(Statics.Config.IniReadValue("base58-prefix"))))
            {
                result["error"] = "Invalid Address";
                return;
            }

            var worker = new ConnectedWorker();
            worker.Address = address;
            worker.LastSeen = DateTime.Now;
            worker.LastDifficulty = uint.Parse(Statics.Config.IniReadValue("base-difficulty"));
            worker.CurrentBlock = Statics.CurrentBlockHeight;

            Logger.Log(Logger.LogLevel.General, "Adding {0} to connected clients", guid);

            result["id"] = guid;

            var seed = 0;

            job["blob"] = Helpers.GenerateUniqueWork(ref seed);

            job["job_id"] = Guid.NewGuid().ToString();

            var shareJob = new ShareJob();
            shareJob.CurrentDifficulty = worker.LastDifficulty;
            shareJob.Seed = seed;
            worker.JobSeed.Add(new KeyValuePair<string, ShareJob>((string) job["job_id"], shareJob));

            job["target"] =
                BitConverter.ToString(
                        BitConverter.GetBytes(Helpers.GetTargetFromDifficulty((uint) shareJob.CurrentDifficulty)))
                    .Replace("-", "");

            Logger.Log(Logger.LogLevel.General, "Sending new work with target {0}", (string) job["target"]);

            result["job"] = job;
            result["status"] = "OK";

            response["result"] = result;

            worker.NewJobRequest();

            Program..ConnectedClients.Add(guid, worker);

            // Add a new client in the database.
            if (Program.RedisPoolDatabase.Miners.Any(x => x.Address == worker.Address))
            {
                var miner = Program.RedisPoolDatabase.Miners.First(x => x.Address == worker.Address);
                var minerWorker = new MinerWorker(guid, miner.Identifier, 0);
                minerWorker.NewJobRequest();
                miner.MinersWorker.Add(guid);
                Program.RedisPoolDatabase.SaveChanges(miner);
                Program.RedisPoolDatabase.SaveChanges(minerWorker);
            }
            else
            {
                var miner = new Miner(worker.Address, 0);
                var minerWorker = new MinerWorker(guid, miner.Identifier, 0);
                minerWorker.NewJobRequest();
                miner.MinersWorker.Add(guid);
                Program.RedisPoolDatabase.SaveChanges(miner);
                Program.RedisPoolDatabase.SaveChanges(minerWorker);
            }

            Logger.Log(Logger.LogLevel.Verbose, "Finished login response");
        }

        public async void AcceptTcpClient(TcpClient client)
        {
            while (true)
            {
                var sRequest = "";
                var abort = false;
                while (client.GetStream().DataAvailable)
                {
                    var b = new byte[1];
                    await client.GetStream().ReadAsync(b, 0, 1);
                    abort = true;
                    if (b[0] == 0x0a)
                    {
                        abort = false;
                        break;
                    }

                    sRequest += Encoding.ASCII.GetString(b);
                    if (sRequest.Length > 1024 * 10 || sRequest.Length - sRequest.Trim().Length > 128)
                        break;
                }

                if (abort)
                {
                    client.Close();
                    break;
                }

                if (sRequest == "")
                    continue;
                var guid = "";
                var s = AcceptClient(sRequest, ((IPEndPoint) client.Client.RemoteEndPoint).Address.ToString(),
                    ref abort, ref guid);

                // Abort signal, close the socket.
                if (abort)
                {
                    client.Close();
                    break;
                }

                // Append newline character
                s += "\n";

                // Convert to bytes
                var byteArray = Encoding.UTF8.GetBytes(s);

                // Asyncronously write to the miner.
                await client.GetStream().WriteAsync(byteArray, 0, byteArray.Length);

                // Add current miner to list.
                if (Program.ConnectedClients[guid].TcpClient == null)
                {
                    Program.ConnectedClients[guid].TcpClient = client;
                }
            }
        }


        public async void AcceptHttpClient(
            HttpListenerContext client)
        {
            var sRequest = Helpers.GetRequestBody(client.Request);
            //sRequest = Helpers.GetRequestBody(client.Request);
            if (sRequest == "")
                return;

            var close = false;

            var guid = "";
            var s = AcceptClient(sRequest, client.Request.UserHostAddress, ref close, ref guid);
            var byteArray = Encoding.UTF8.GetBytes(s);

            client.Response.ContentType = "application/json";

            client.Response.ContentLength64 = byteArray.Length;
            client.Response.OutputStream.Write(byteArray, 0, byteArray.Length);

            /* foreach (var b in client.connections)
             {
                                                                                                               
             } */
            //client.
            client.Response.OutputStream.Close();
            //  if (close)
            client.Response.Close();

            if (close)
                client.Request.InputStream.Dispose();
        }

        public string AcceptClient(string sRequest, string ipAddress, ref bool abort, ref string guid)
        {
            try
            {
                var request = JObject.Parse(sRequest);
                var response = new JObject
                {
                    ["id"] = 0,
                    ["jsonrpc"] = "2.0",
                    ["error"] = null
                };


                if ((string) request["method"] == "login")
                {
                    guid = Guid.NewGuid().ToString();
                }

                else
                {
                    guid = (string) request["params"]["id"];

                    if (!Statics.ConnectedClients.ContainsKey(guid))
                    {
                        response["error"] = "Not authenticated yet!";
                        return JsonConvert.SerializeObject(response);
                    }
                }

                switch ((string) request["method"])
                {
                    case "login":
                        GenerateLoginResponse(ref response, guid, (string) request["params"]["login"]);
                        break;
                    case "getjob":
                        GenerateGetJobResponse(ref response, guid);
                        break;
                    case "submit":
                        abort = GenerateSubmitResponse(ref response, (string) request["params"]["job_id"], guid,
                            Helpers.StringToByteArray((string) request["params"]["nonce"]),
                            (string) request["params"]["result"], ipAddress);
                        break;
                }

                return JsonConvert.SerializeObject(response);
            }

            catch (Exception e)
            {
                Logger.Log(Logger.LogLevel.Error, e.ToString());
                abort = true;
                return "";
            }
        }
    }
}