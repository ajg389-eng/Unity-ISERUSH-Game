using System;

public static class ProductionSimulationCheckProgram
{
    public static int Main()
    {
        try { Console.WriteLine(ProductionSimulationChecks.Run()); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
