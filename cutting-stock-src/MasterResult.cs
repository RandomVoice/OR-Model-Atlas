namespace CuttingStockLearning.Models
{
    public class MasterResult
    {
        public double ObjectiveValue { get; set; }

        public double[] Duals { get; set; }

        public double[] CutValues { get; set; }

        public double[] ProducedAmounts { get; set; }

        public bool IsOptimal { get; set; }
    }
}
