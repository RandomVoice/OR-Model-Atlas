namespace CuttingStockLearning.Models
{
    public static class SampleData
    {
        public static CuttingStockData Create()
        {
            return new CuttingStockData
            {
                RollWidth = 110,

                ItemSizes = new[]
                {
                    20,
                    45,
                    50,
                    55,
                    75
                },

                Demands = new[]
                {
                    48,
                    35,
                    24,
                    10,
                    8
                }
            };
        }
    }
}
