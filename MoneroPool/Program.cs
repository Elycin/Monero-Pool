using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;

namespace MoneroPool
{
    internal class Program
    {
        // Public classes to application
        public static Configuration Configuration;
        public static ConfigurationOptions RedisConfigurationOptions;
        public static volatile uint TotalShares;
        public static volatile StaticsLock Lock;
        public static volatile JObject CurrentBlockTemplate;
        public static volatile int CurrentBlockHeight;
        public static volatile int ReserveSeed;
        public static volatile JsonRPC DaemonJson;
        public static volatile JsonRPC WalletJson;
        public static volatile List<PoolBlock> BlocksPendingSubmition;
        public static volatile List<PoolBlock> BlocksPendingPayment;
        public static volatile Dictionary<string, ConnectedWorker> ConnectedClients =
            new Dictionary<string, ConnectedWorker>();
        public static volatile RedisPoolDatabase RedisPoolDatabase;
        public static volatile PoolHashRateCalculation HashRate;

        /// <summary>
        /// Application Entry Point.
        /// </summary>
        /// <param name="args"></param>
        private static void Main(string[] args)
        {
            // Get a new global configuration instance.
            Configuration = new Configuration();

            // Initialize redis configuration
            RedisConfigurationOptions = new ConfigurationOptions
            {
                ResolveDns = true
            };

            // Add redis connection.
            RedisConfigurationOptions.EndPoints.Add(
                Configuration.GetRedisAddress(),
                Configuration.GetRedisPort()
            );

            // Initialize Redis Connection.
            InitializeRedis();

            // Initialize Block Objects.
            HashRate = new PoolHashRateCalculation();
            BlocksPendingPayment = new List<PoolBlock>();
            BlocksPendingSubmition = new List<PoolBlock>();
            ConnectedClients = new Dictionary<string, ConnectedWorker>();
            DaemonJson = new JsonRPC(Configuration.GetDaemonRpc());
            WalletJson = new JsonRPC(Configuration.GetWalletRpc());

            // Create local instances to keep classes in memory.
            var backgroundSaticUpdater = new BackgroundStaticUpdater();
            var blockPayment = new BlockPayment();
            var blockSubmitter = new BlockSubmitter();
            var difficultyRetargeter = new DifficultyRetargeter();
            var cryptoNightPool = new CryptoNightPool();

            // Start routines.
            backgroundSaticUpdater.Start();
            blockPayment.Start();
            blockSubmitter.Start();
            difficultyRetargeter.Start();
            cryptoNightPool.Start();


            // Pointless loop to keep the application running.
            while (true)
            {
                Thread.Sleep(Timeout.Infinite);
            }
        }

        /// <summary>
        /// Initialize the redis database connection.
        /// </summary>
        public static void InitializeRedis()
        {
            try
            {
                RedisPoolDatabase = new RedisPoolDatabase(
                    ConnectionMultiplexer.Connect(RedisConfigurationOptions)
                        .GetDatabase(Configuration.GetRedisDatabase())
                );
            }
            catch (RedisConnectionException)
            {
                Logger.Log(Logger.LogLevel.Error, "Redis connection failed.");
                Environment.Exit(0);
            }
        }
    }
}