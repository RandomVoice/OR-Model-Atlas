using System.Linq;

namespace CuttingStockLearning.Models
{
    public class Pattern
    {
        public int Id { get; }

        public int Cost { get; }

        public int[] Fill { get; }

        public Pattern(
            int id,
            int cost,
            int[] fill)
        {
            Id = id;
            Cost = cost;
            Fill = fill;
        }

        public override string ToString()
        {
            return $"Pattern {Id}: [{string.Join(", ", Fill)}]";
        }
    }
}
