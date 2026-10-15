using System;
using System.Collections.Generic;
using System.Text;
using Sim = KitchenProduction.ProductionSimulation;

// Runs independently of Unity, using the exact gameplay/forecast implementation.
// powershell -File Tools/TestProductionSimulation.ps1
public static class ProductionSimulationChecks
{
    static int checks;
    static void Check(bool condition, string description)
    {
        checks++;
        if (!condition) throw new Exception(description);
    }

    static Sim.Node Source(int item, long period = 1000)
    {
        return new Sim.Node { Kind = Sim.NodeKind.Source, SourceItems = new[] { item }, SourcePeriodTicks = period };
    }

    static Sim.Node Processor(int input, int output, long cycle = 4000)
    {
        return new Sim.Node { Kind = Sim.NodeKind.Processor, OutputItem = output, CycleTicks = cycle,
            Inputs = new[] { new Sim.Input { Item = input, Capacity = 4, Amount = 1 } } };
    }

    static Sim.Node Pickup(int capacity = 4)
    {
        return new Sim.Node { Kind = Sim.NodeKind.Pickup, OutputCapacity = capacity };
    }

    static Sim.Edge Edge(int from, int to, int item, int flow = 0)
    {
        return new Sim.Edge { From = from, To = to, Item = item, Flow = flow };
    }

    static Sim Create(Sim.Node[] nodes, Sim.Edge[] edges, int workerCount = 2, long travel = 1000,
        long interaction = 500, int itemCount = 3, int flowCount = 1)
    {
        var workers = new Sim.Worker[workerCount];
        for (int w = 0; w < workerCount; w++)
        {
            var matrix = new long[nodes.Length, nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
                for (int j = 0; j < nodes.Length; j++) matrix[i, j] = Math.Abs(i - j) * travel;
            workers[w] = new Sim.Worker { Origin = 0, Flow = w % flowCount,
                TravelTicks = matrix, PickupTicks = interaction, PlacementTicks = interaction };
        }
        var sim = new Sim(new Sim.Definition { Nodes = nodes, Edges = edges, Workers = workers,
            ItemCount = itemCount, FlowCount = flowCount });
        for (int i = 0; i < sim.Stock.Length; i++) sim.Stock[i] = 10000;
        return sim;
    }

    static Sim Line(int workers = 2, long travel = 1000, long interaction = 500)
    {
        return Create(new[] { Source(0), Processor(0, 1), Pickup() },
            new[] { Edge(0, 1, 0), Edge(1, 2, 1) }, workers, travel, interaction);
    }

    static string State(Sim s)
    {
        var text = new StringBuilder().Append(s.Now);
        foreach (long d in s.Delivered) text.Append('|').Append(d);
        foreach (int stock in s.Stock) text.Append('|').Append(stock);
        foreach (int stock in s.ReservedStock) text.Append('|').Append(stock);
        foreach (Sim.Buffer b in s.Buffers)
        {
            text.Append('|').Append(b.Output).Append(',').Append(b.ReservedOutput).Append(',')
                .Append(b.Processing).Append(',').Append(b.FinishAt).Append(',').Append(b.CreditTicks).Append(',').Append(b.ReservedCredits);
            foreach (int v in b.Inputs) text.Append(',').Append(v);
            foreach (int v in b.ReservedInputs) text.Append(',').Append(v);
        }
        foreach (Sim.Trip t in s.Trips)
            text.Append('|').Append(t.Phase).Append(',').Append(t.Source).Append(',').Append(t.Destination)
                .Append(',').Append(t.Location).Append(',').Append(t.CargoItem).Append(',').Append(t.CargoUnits)
                .Append(',').Append(t.DueAt).Append(',').Append(t.ReadyAt);
        return text.ToString();
    }

    static void Invariants(Sim s)
    {
        for (int i = 0; i < s.Buffers.Length; i++)
        {
            Sim.Buffer b = s.Buffers[i];
            Sim.Node n = s.Plan.Nodes[i];
            Check(b.Output >= 0 && b.Output <= n.OutputCapacity, "Output buffer overflow/underflow");
            Check(b.ReservedOutput >= 0 && b.ReservedOutput <= b.Output, "Double-claimed output");
            Check(b.ReservedCredits >= 0 && b.CreditTicks >= 0, "Source budget underflow");
            int total = b.Processing + b.Output;
            for (int item = 0; item < b.Inputs.Length; item++)
            {
                Check(b.Inputs[item] >= 0 && b.ReservedInputs[item] >= 0, "Input/reservation underflow");
                total += b.Inputs[item] + b.ReservedInputs[item];
            }
            foreach (Sim.Input input in n.Inputs)
                Check(b.Inputs[input.Item] + b.ReservedInputs[input.Item] <= input.Capacity, "Input overflow");
            if (n.SharedCapacity > 0) Check(total <= n.SharedCapacity, "Shared physical slot overflow");
            if (n.Kind == Sim.NodeKind.Pickup && !s.DrainPickup) Check(total <= n.OutputCapacity, "Pickup overbooked");
        }
        for (int i = 0; i < s.Stock.Length; i++)
            Check(s.Stock[i] >= 0 && s.ReservedStock[i] >= 0 && s.ReservedStock[i] <= s.Stock[i], "Stock overbooked");
    }

    public static string Run()
    {
        checks = 0;
        var reports = new List<string>();

        Sim whole = Line(); whole.DrainPickup = true;
        Sim sliced = whole.Copy();
        whole.AdvanceTo(180000);
        for (long t = 17; t < 180000; t += 17) sliced.AdvanceTo(t);
        sliced.AdvanceTo(180000);
        Check(State(whole) == State(sliced), "Frame sizes changed the simulation outcome");
        Check(whole.Delivered[0] > 0, "Line never produced finished items");
        reports.Add("PASS: one advance and 10,589 frame advances produce identical complete state");

        Sim live = Line(); live.DrainPickup = true;
        live.AdvanceTo(19873);
        string original = State(live);
        long expected = live.Forecast(60000, false, true)[0];
        Check(original == State(live), "Forecast mutated the live state");
        long before = live.Delivered[0];
        live.AdvanceTo(live.Now + 60000);
        Check(live.Delivered[0] - before == expected, "Forecast did not match execution");
        reports.Add("PASS: forecast equals real execution from a snapshot with trips in progress");

        Sim fast = Line(1, 500, 500); fast.DrainPickup = true;
        Sim far = Line(1, 15000, 500); far.DrainPickup = true;
        Sim slowHands = Line(1, 500, 10000); slowHands.DrainPickup = true;
        fast.AdvanceTo(600000); far.AdvanceTo(600000); slowHands.AdvanceTo(600000);
        Check(fast.Delivered[0] > far.Delivered[0], "Walking time did not limit output");
        Check(fast.Delivered[0] > slowHands.Delivered[0], "Interaction time did not limit output");
        reports.Add("PASS: longer walking and interaction reduce actual scheduled deliveries");

        Sim merge = Create(new[] { Source(0, 500), Processor(0, 1, 6000), Processor(0, 1, 12000),
            Processor(1, 2, 4000), Pickup() }, new[] { Edge(0, 1, 0), Edge(0, 2, 0),
            Edge(1, 3, 1), Edge(2, 3, 1), Edge(3, 4, 2) }, 6, 100);
        merge.DrainPickup = true;
        merge.AdvanceTo(120000);
        before = merge.Delivered[0];
        merge.AdvanceTo(720000);
        long mergedOutput = merge.Delivered[0] - before;
        Check(mergedOutput > 100 && mergedOutput <= 150, "Parallel supplies were treated as the slowest station: " + mergedOutput);
        Invariants(merge);
        reports.Add("PASS: 10/min + 5/min producers merge into a 15/min recipe (" + mergedOutput + "/10min delivered)");

        Sim.Node twoInput = Processor(0, 2, 2000);
        twoInput.SharedCapacity = 4;
        twoInput.Inputs = new[] { new Sim.Input { Item = 0, Capacity = 4 }, new Sim.Input { Item = 1, Capacity = 4 } };
        Sim shared = Create(new[] { Source(0), Source(1), twoInput, Pickup() },
            new[] { Edge(0, 2, 0), Edge(1, 2, 1), Edge(2, 3, 2) }, 4, 100);
        shared.DrainPickup = true;
        for (long t = 0; t <= 120000; t += 113) { shared.AdvanceTo(t); Invariants(shared); }
        Check(shared.Delivered[0] > 10, "A shared buffer filled with one ingredient and deadlocked");
        reports.Add("PASS: shared slots retain space for the second ingredient without overbooking");

        foreach (int slotCapacity in new[] { 4, 8 })
        {
            Sim.Node cuttingBoard = Processor(0, 1, 4000);
            cuttingBoard.OutputCapacity = slotCapacity;
            cuttingBoard.SharedCapacity = slotCapacity;
            cuttingBoard.Inputs[0].Capacity = slotCapacity;
            Sim cutBoard = Create(new[] { Source(0, 1000), cuttingBoard, Pickup(0) },
                new[] { Edge(0, 1, 0), Edge(1, 2, 1) }, 1, 100);
            for (long t = 0; t <= 120000; t += 257) { cutBoard.AdvanceTo(t); Invariants(cutBoard); }
            cutBoard.AdvanceTo(120000);
            int occupied = cutBoard.Buffers[1].Output + cutBoard.Buffers[1].Processing;
            for (int item = 0; item < cutBoard.Buffers[1].Inputs.Length; item++)
                occupied += cutBoard.Buffers[1].Inputs[item] + cutBoard.Buffers[1].ReservedInputs[item];
            Check(occupied == slotCapacity, "Shared cutting board did not fill exactly its " + slotCapacity + " slots");
        }
        reports.Add("PASS: cutting board shared input/output buffer caps at four MK1 slots and eight MK2 slots");

        Sim stock = Create(new[] { Source(0, 50), Source(0, 50), Processor(0, 1, 1000), Pickup(50) },
            new[] { Edge(0, 2, 0), Edge(1, 2, 0), Edge(2, 3, 1) }, 6, 100);
        stock.Stock[0] = 10;
        for (long t = 0; t <= 120000; t += 97) { stock.AdvanceTo(t); Invariants(stock); }
        Check(stock.Delivered[0] == 10 && stock.Stock[0] == 0, "Shared stock was duplicated or lost");
        reports.Add("PASS: competing sources/workers deliver exactly the ten available ingredients");

        Sim full = Line(4, 100); full.AdvanceTo(180000); Invariants(full);
        Check(full.Delivered[0] == 4, "Full pickup did not stop further deliveries");
        long next = full.Forecast(60000)[0];
        before = full.Delivered[0]; full.AdvanceTo(full.Now + 60000);
        Check(next == 0 && full.Delivered[0] == before, "Forecast ignored pickup capacity");
        full.Buffers[2].Inputs[1]--;
        full.AdvanceTo(full.Now + 60000);
        Check(full.Delivered[0] == before + 1, "Collection failed to resume production");
        reports.Add("PASS: full pickup blocks output and a collection opens exactly one completion slot");

        Sim unreachable = Line();
        foreach (Sim.Worker worker in unreachable.Plan.Workers) worker.TravelTicks[0, 1] = -1;
        unreachable.AdvanceTo(120000);
        Check(unreachable.Delivered[0] == 0, "Unreachable station accepted a physical transfer");
        reports.Add("PASS: an unreachable flow node produces no phantom deliveries");

        Sim cancelled = Line(2, 10000);
        foreach (Sim.Trip trip in cancelled.Trips) trip.Location = 2;
        cancelled.AdvanceTo(1000);
        cancelled.Stock[0] = 0;
        cancelled.ReconcileStockReservations();
        cancelled.AdvanceTo(120000);
        Invariants(cancelled);
        Check(cancelled.Delivered[0] == 0, "Removed stock was delivered anyway");
        reports.Add("PASS: external stock removal releases unpicked reservations without duplicating items");

        Sim separate = Create(new[] { Source(0), Processor(0, 1), Pickup(100),
            Source(0), Processor(0, 2), Pickup(100) }, new[] { Edge(0, 1, 0, 0), Edge(1, 2, 1, 0),
            Edge(3, 4, 0, 1), Edge(4, 5, 2, 1) }, 4, 100, 500, 3, 2);
        separate.AdvanceTo(120000);
        Check(separate.Delivered[0] == separate.Buffers[2].Inputs[1], "First flow counted another flow's deliveries");
        Check(separate.Delivered[1] == separate.Buffers[5].Inputs[2], "Second flow counted another flow's deliveries");
        Check(separate.Delivered[0] > 0 && separate.Delivered[1] > 0, "A duplicate recipe/station flow did not run");
        reports.Add("PASS: station identity and delivered output remain separate across flows");
        return string.Join(Environment.NewLine, reports) + Environment.NewLine + checks + " assertions passed.";
    }
}
