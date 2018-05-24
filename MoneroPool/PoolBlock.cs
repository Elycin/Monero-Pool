namespace MoneroPool
{
    // Structure to hold the block of the pool.
    public class PoolBlock
    {
        // Constructor
        public PoolBlock(byte[] blockData, int blockHeight, string blockHash, string founder)
        {
            BlockData = blockData;
            BlockHash = blockHash;
            BlockHeight = blockHeight;
            Founder = founder;
        }

        // Getters
        public byte[] BlockData { get; set; }
        public int BlockHeight { get; set; }
        public string BlockHash { get; set; }
        public string Founder { get; set; }
    }
}