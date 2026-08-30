using System;
using System.Linq;

using CuttingStockLearning.ColumnGeneration;
using CuttingStockLearning.Exercises;

namespace CuttingStockLearning
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // Run the exercises with:  dotnet run -- exercises
            if (args.Any(a =>
                    string.Equals(a, "exercises",
                        StringComparison.OrdinalIgnoreCase)))
            {
                ExerciseRunner.RunAll();
                return;
            }

            var solver = new ColumnGenerationSolver();
            solver.Run();
        }
    }
}
