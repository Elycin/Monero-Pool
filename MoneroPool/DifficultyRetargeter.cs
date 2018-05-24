﻿using System.Threading;
using System.Threading.Tasks;

namespace MoneroPool
{
    public class DifficultyRetargeter
    {
        public async void Start()
        {
            await Task.Yield();
            while (true)
            {
                Thread.Sleep(int.Parse(Statics.Config.IniReadValue("diffiulty-retarget")) * 1000);
                var localCopy = Statics.ConnectedClients;
                foreach (var connectedClient in localCopy)
                    if (connectedClient.Value.ShareDifficulty.Count > 4)
                        connectedClient.Value.PendingDifficulty =
                            Helpers.WorkerVardiffDifficulty(connectedClient.Value);
            }
        }
    }
}