using CuttingStockLearning.Models;
using ILOG.Concert;
using ILOG.CPLEX;

namespace CuttingStockLearning.Pricing
{
    public class PricingProblem
    {
        public PricingResult Solve(
            CuttingStockData data,
            double[] duals)
        {
            using var cplex = new Cplex();

            //
            // Use[i]
            //
            INumVar[] use =
                cplex.IntVarArray(
                    data.NumberOfItems,
                    0,
                    int.MaxValue);

            //
            // Objective
            //
            ILinearNumExpr objective =
                cplex.LinearNumExpr();

            for (int i = 0; i < data.NumberOfItems; i++)
            {
                objective.AddTerm(
                    duals[i],
                    use[i]);
            }

            cplex.AddMaximize(objective);

            //
            // Roll Width Constraint
            //
            ILinearNumExpr widthExpr =
                cplex.LinearNumExpr();

            for (int i = 0; i < data.NumberOfItems; i++)
            {
                widthExpr.AddTerm(
                    data.ItemSizes[i],
                    use[i]);
            }

            cplex.AddLe(
                widthExpr,
                data.RollWidth);

            if (!cplex.Solve())
            {
                return new PricingResult
                {
                    IsOptimal = false
                };
            }

            int[] fill =
                new int[data.NumberOfItems];

            for (int i = 0; i < data.NumberOfItems; i++)
            {
                fill[i] =
                    (int)System.Math.Round(
                        cplex.GetValue(use[i]));
            }

            double reducedCost =
                1.0 - cplex.ObjValue;

            Pattern pattern =
                new Pattern(
                    -1,
                    1,
                    fill);

            return new PricingResult
            {
                IsOptimal = true,
                ReducedCost = reducedCost,
                Pattern = pattern
            };
        }
    }
}
