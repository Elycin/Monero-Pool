using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace MoneroPool
{
    public struct ShareJob
    {
        public int Seed;
        private ulong _currentDifficulty;

        public ulong CurrentDifficulty
        {
            get => _currentDifficulty;
            set
            {
                if (value > (uint) Statics.CurrentBlockTemplate["difficulty"])
                    value = (uint) Statics.CurrentBlockTemplate["difficulty"];
                if (value <= uint.Parse(Statics.Config.IniReadValue("base-difficulty")))
                    value = uint.Parse(Statics.Config.IniReadValue("base-difficulty"));
                _currentDifficulty = value;
            }
        }

        private List<int> _submittedShares;

        public List<int> SubmittedShares
        {
            get
            {
                if (_submittedShares == null)
                    _submittedShares = new List<int>();
                return _submittedShares;
            }
        }
    }

    public class ConnectedWorker
    {
        private DateTime _lastjoborshare;
        private DateTime _share;

        public ConnectedWorker()
        {
            JobSeed = new List<KeyValuePair<string, ShareJob>>();
            ShareDifficulty = new List<KeyValuePair<TimeSpan, ulong>>();
        }

        public string Address { get; set; }
        public DateTime LastSeen { get; set; }
        public List<KeyValuePair<TimeSpan, ulong>> ShareDifficulty { get; }
        public DateTime LastShare { get; set; }

        public TcpClient TcpClient { get; set; }

        public List<KeyValuePair<string, ShareJob>> JobSeed { get; set; }
        public int CurrentBlock { get; set; }

        public int TotalShares { get; set; }
        public int RejectedShares { get; set; }

        public uint LastDifficulty { get; set; }
        public uint PendingDifficulty { get; set; }


        public void NewJobRequest()
        {
            _lastjoborshare = DateTime.Now;
        }

        public void ShareRequest(ulong difficulty)
        {
            _share = DateTime.Now;
            ShareDifficulty.Add(new KeyValuePair<TimeSpan, ulong>(_share - _lastjoborshare, difficulty));
            _lastjoborshare = _share;
            LastShare = DateTime.Now;
        }
    }
}