namespace CuttingStockLearning.Models
{
    public class PricingResult
    {
        public bool IsOptimal { get; set; }

        public double ReducedCost { get; set; }

        public Pattern Pattern { get; set; }
    }
}
