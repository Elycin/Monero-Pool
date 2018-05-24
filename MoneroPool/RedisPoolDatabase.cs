using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace MoneroPool
{
    public class RedisPoolDatabase
    {
        // Class Getters
        public IDatabase RedisDatabase { get; set; }
        public PoolInformation Information { get; set; }
        public List<Block> Blocks { get; private set; }
        public List<Miner> Miners { get; private set; }
        public List<BlockReward> BlockRewards { get; private set; }
        public List<Share> Shares { get; private set; }
        public List<MinerWorker> MinerWorkers { get; private set; }
        public List<Ban> Bans { get; private set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="redisDatabase"></param>
        public RedisPoolDatabase(IDatabase redisDatabase)
        {
            // Inherit
            RedisDatabase = redisDatabase;

            // Update Lists
            InitializeLists();
        }

        /// <summary>
        /// Purge all lists.
        /// </summary>
        public void InitializeLists()
        {
            // Re-initialize all objects.
            Blocks = new List<Block>();
            Miners = new List<Miner>();
            BlockRewards = new List<BlockReward>();
            Shares = new List<Share>();
            MinerWorkers = new List<MinerWorker>();
            Information = new PoolInformation();
            Bans = new List<Ban>();

            // Deserialize all objects
            Deserialize(Blocks);
            Deserialize(Miners);
            Deserialize(BlockRewards);
            Deserialize(Shares);
            Deserialize(MinerWorkers);
            Deserialize(Information);
            Deserialize(Bans);
        }

        /// <summary>
        /// Deserialize Object
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        private void Deserialize<T>(T obj)
        {
            var t = typeof(T);
            var hashEntries = RedisDatabase.HashGetAll(t.Name);

            foreach (var property in t.GetProperties())
            {
                try
                {
                    if (property.PropertyType == typeof(int))
                    {
                        property.SetValue(
                            obj,
                            JsonConvert.DeserializeObject<int>(
                                hashEntries.First(x => x.Name == property.Name).Value
                            )
                        );
                    }
                    else if (property.PropertyType == typeof(List<string>))
                    {
                        property.SetValue(
                            obj,
                            JsonConvert.DeserializeObject<List<string>>(
                                hashEntries.First(x => x.Name == property.Name).Value
                            )
                        );
                    }
                    else
                    {
                        property.SetValue(
                            obj,
                            JsonConvert.DeserializeObject(
                                hashEntries.First(x => x.Name == property.Name).Value
                            )
                        );
                    }
                }
                catch
                {
                    // ignored
                }
            }
        }

        /// <summary>
        /// Deserialize Object List
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        private void Deserialize<T>(List<T> obj)
        {
            var t = typeof(T);

            // Iterate through each value on Redis.
            foreach (string redisValue in RedisDatabase.SortedSetRangeByScore(t.GetTypeInfo().Name))
            {
                var tObject = (T) Activator.CreateInstance(t);
                var hashEntries = RedisDatabase.HashGetAll(redisValue);
                foreach (var currentProperty in t.GetProperties())
                {
                    // Try to determine the property.
                    try
                    {
                        if (currentProperty.PropertyType == typeof(int))
                        {
                            currentProperty.SetValue(
                                tObject,
                                JsonConvert.DeserializeObject<int>(
                                    hashEntries.First(x => x.Name == currentProperty.Name).Value
                                )
                            );
                        }
                        else if (currentProperty.PropertyType == typeof(List<string>))
                        {
                            currentProperty.SetValue(
                                tObject,
                                JsonConvert.DeserializeObject<List<string>>(hashEntries
                                    .First(x => x.Name == currentProperty.Name).Value
                                )
                            );
                        }
                        else
                        {
                            currentProperty.SetValue(
                                tObject,
                                JsonConvert.DeserializeObject(
                                    hashEntries.First(x => x.Name == currentProperty.Name).Value
                                )
                            );
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                }

                // Add the current tObject
                obj.Add(tObject);
            }
        }

        /// <summary>
        /// Serialize an object.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        private void Serialize<T>(T obj)
        {
            var t = typeof(T);
            var properties = t.GetProperties();
            var hashEntries = new HashEntry[properties.Length];
            var i = 0;

            foreach (var property in properties)
            {
                hashEntries[i] = new HashEntry(property.Name, JsonConvert.SerializeObject(property.GetValue(obj)));
                i++;
            }

            RedisDatabase.HashSet(t.Name, hashEntries);
        }

        /// <summary>
        /// Save object changes.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        private void SaveChanges<T>(T obj)
        {
            var t = typeof(T);
            var properties = t.GetProperties();
            var fields = t.GetFields();
            var hashEntries = new HashEntry[properties.Length + fields.Length];
            var i = 0;
            foreach (var property in properties)
            {
                hashEntries[i] = new HashEntry(property.Name, JsonConvert.SerializeObject(property.GetValue(obj)));
                i++;
            }

            var guid = t.GetProperty("Identifier").GetValue(obj).ToString();
            RedisDatabase.SortedSetAdd(t.GetTypeInfo().Name, guid, RedisDatabase.SortedSetLength(t.GetTypeInfo().Name));
            RedisDatabase.HashSet(guid, hashEntries);
        }

        /// <summary>
        /// save miner changes.
        /// </summary>
        /// <param name="miner"></param>
        public void SaveChanges(Miner miner)
        {
            SaveChanges<Miner>(miner);

            for (var i = 0; i < Miners.Count; i++)
                if (Miners[i].Identifier == miner.Identifier)
                {
                    Miners.RemoveAt(i);
                    Miners.Insert(i, miner);
                    return;
                }

            Miners.Add(miner);
        }

        /// <summary>
        /// Save worker information
        /// </summary>
        /// <param name="minerWorker"></param>
        public void SaveChanges(MinerWorker minerWorker)
        {
            SaveChanges<MinerWorker>(minerWorker);


            for (var i = 0; i < MinerWorkers.Count; i++)
                if (MinerWorkers[i].Identifier == minerWorker.Identifier)
                {
                    MinerWorkers.RemoveAt(i);
                    MinerWorkers.Insert(i, minerWorker);
                    return;
                }

            MinerWorkers.Add(minerWorker);
        }

        /// <summary>
        /// Save share.
        /// </summary>
        /// <param name="share"></param>
        public void SaveChanges(Share share)
        {
            SaveChanges<Share>(share);
            for (var i = 0; i < Shares.Count; i++)
                if (Shares[i].Identifier == share.Identifier)
                {
                    Shares.RemoveAt(i);
                    Shares.Insert(i, share);
                    return;
                }

            Shares.Add(share);
        }

        /// <summary>
        /// Save block reward.
        /// </summary>
        /// <param name="blockReward"></param>
        public void SaveChanges(BlockReward blockReward)
        {
            SaveChanges<BlockReward>(blockReward);

            for (var i = 0; i < BlockRewards.Count; i++)
                if (BlockRewards[i].Identifier == blockReward.Identifier)
                {
                    BlockRewards.RemoveAt(i);
                    BlockRewards.Insert(i, blockReward);
                    return;
                }

            BlockRewards.Add(blockReward);
        }

        /// <summary>
        /// Save block information.
        /// </summary>
        /// <param name="block"></param>
        public void SaveChanges(Block block)
        {
            SaveChanges<Block>(block);

            for (var i = 0; i < Blocks.Count; i++)
                if (Blocks[i].Identifier == block.Identifier)
                {
                    Blocks.RemoveAt(i);
                    Blocks.Insert(i, block);
                    return;
                }

            Blocks.Add(block);
        }

        /// <summary>
        /// Save ban information.
        /// </summary>
        /// <param name="ban"></param>
        public void SaveChanges(Ban ban)
        {
            SaveChanges<Ban>(ban);

            for (var i = 0; i < Bans.Count; i++)
                if (Bans[i].Identifier == ban.Identifier)
                {
                    Bans.RemoveAt(i);
                    Bans.Insert(i, ban);
                    return;
                }

            Bans.Add(ban);
        }

        /// <summary>
        /// Save pool information.
        /// </summary>
        /// <param name="poolInformation"></param>
        public void SaveChanges(PoolInformation poolInformation)
        {
            Serialize(poolInformation);
            Information = poolInformation;
        }

        /// <summary>
        /// Remove an ambigious object from the pool.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        private void Remove<T>(T obj)
        {
            var t = typeof(T);
            var guid = t.GetProperty("Identifier").GetValue(obj).ToString();
            RedisDatabase.SortedSetRemove(t.Name, guid);
            RedisDatabase.KeyDelete(guid);
        }

        /// <summary>
        /// Remove a worker form the pool.
        /// </summary>
        /// <param name="worker"></param>
        public void Remove(MinerWorker worker)
        {
            Remove<MinerWorker>(worker);
            MinerWorkers.Remove(worker);
        }

        /// <summary>
        /// Remove a ban from the pool.
        /// </summary>
        /// <param name="ban"></param>
        public void Remove(Ban ban)
        {
            Remove<Ban>(ban);
            Bans.Remove(ban);
        }
    }

    public class Ban
    {
        public Ban()
        {
            Identifier = Guid.NewGuid().ToString();
        }

        public string IpBan { get; set; }
        public string AddressBan { get; set; }
        public string Identifier { get; set; }
        public DateTime Begin { get; set; }
        public int Minutes { get; set; }
    }

    public class PoolInformation
    {
        public int LastPaidBlock { get; set; }
        public int CurrentBlock { get; set; }
        public double NewtworkHashRate { get; set; }
        public double PoolHashRate { get; set; }
        public double SharesPerSecond { get; set; }
        public ulong RoundShares { get; set; }
        public int BaseDificulty { get; set; }
    }

    public class Block
    {
        /// <summary>
        /// Constructor without variables
        /// </summary>
        public Block()
        {
        }

        /// <summary>
        /// Constructor with variables.
        /// </summary>
        /// <param name="blockHeight"></param>
        public Block(int blockHeight)
        {
            Identifier = Guid.NewGuid().ToString();
            BlockRewards = new List<string>();
            BlockHeight = blockHeight;
        }

        // Getters
        public string Identifier { get; set; }
        public string Founder { get; set; }
        public bool Found { get; set; }
        public int BlockHeight { get; set; }
        public bool Orphan { get; set; }
        public DateTime FoundDateTime { get; set; }
        public List<string> BlockRewards { get; set; }
    }

    public class Miner
    {
        /// <summary>
        /// Constructor without variables.
        /// </summary>
        public Miner()
        {
        }

        /// <summary>
        /// Constructor with variables.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="hashRate"></param>
        public Miner(string address, double hashRate)
        {
            MinersWorker = new List<string>();
            BlockReward = new List<string>();
            Address = address;
            TimeHashRate = new Dictionary<DateTime, double> {{DateTime.Now, hashRate}};
            Identifier = Guid.NewGuid().ToString();
        }

        // Getters
        public string Identifier { get; set; }
        public string Address { get; set; }
        public Dictionary<DateTime, double> TimeHashRate { get; set; }
        public List<string> MinersWorker { get; set; }
        public List<string> BlockReward { get; set; }
        public ulong TotalPaidOut { get; set; }
    }

    public class BlockReward
    {
        /// <summary>
        /// Constructor without variables
        /// </summary>
        public BlockReward()
        {
        }

        /// <summary>
        /// Constructor with variables.
        /// </summary>
        /// <param name="miner"></param>
        /// <param name="block"></param>
        public BlockReward(string miner, string block)
        {
            Shares = new List<string>();
            Block = block;
            Miner = miner;
            Identifier = Guid.NewGuid().ToString();
        }


        // Getters
        public string Identifier { get; set; }
        public string Miner { get; set; }
        public string Block { get; set; }
        public List<string> Shares { get; set; }
    }

    // Structure to hold share data.
    public class Share
    {
        /// <summary>
        /// Constructor without variables.
        /// </summary>
        public Share()
        {
        }

        /// <summary>
        /// Constructor with variables.
        /// </summary>
        /// <param name="blockReward"></param>
        /// <param name="value"></param>
        public Share(string blockReward, double value)
        {
            BlockReward = blockReward;
            Value = value;
            Identifier = Guid.NewGuid().ToString();
        }

        // Getters
        public string Identifier { get; set; }
        public string BlockReward { get; set; }
        public DateTime DateTime { get; set; }
        public double Value { get; set; }
    }

    public class MinerWorker
    {
        // Class Variables.
        private DateTime _lastjoborshare;
        private DateTime _share;
        private List<KeyValuePair<TimeSpan, ulong>> _shareDifficulty;

        // Getters
        public string Identifier { get; set; }
        public DateTime Connected { get; set; }
        public string Miner { get; set; }
        public double HashRate { get; set; }

        /// <summary>
        /// Constructor - No variables.
        /// </summary>
        public MinerWorker()
        {
        }

        /// <summary>
        /// Constructor - With Variables.
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="miner"></param>
        /// <param name="hashRate"></param>
        public MinerWorker(string identifier, string miner, double hashRate)
        {
            Miner = miner;
            HashRate = hashRate;
            Connected = DateTime.Now;
            Identifier = identifier;
            ShareDifficulty = new List<KeyValuePair<TimeSpan, ulong>>();
        }

        /// <summary>
        /// Get Share Difficulty.
        /// </summary>
        public List<KeyValuePair<TimeSpan, ulong>> ShareDifficulty
        {
            get => _shareDifficulty ?? (_shareDifficulty = new List<KeyValuePair<TimeSpan, ulong>>());
            private set => _shareDifficulty = value;
        }

        /// <summary>
        /// Request a new job
        /// </summary>
        public void NewJobRequest()
        {
            _lastjoborshare = DateTime.Now;
        }

        /// <summary>
        /// Request a new share.
        /// </summary>
        /// <param name="difficulty"></param>
        public void ShareRequest(ulong difficulty)
        {
            _share = DateTime.Now;
            ShareDifficulty.Add(new KeyValuePair<TimeSpan, ulong>(_share - _lastjoborshare, difficulty));
            _lastjoborshare = _share;
            HashRate = Helpers.GetMinerWorkerHashRate(this);
        }
    }
}