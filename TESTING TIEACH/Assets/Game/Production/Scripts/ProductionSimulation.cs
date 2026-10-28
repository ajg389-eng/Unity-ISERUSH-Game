using System;
using System.Collections.Generic;

namespace KitchenProduction
{
    // No Unity clock, GameObjects, random choices, callbacks, or frame-dependent
    // movement in this file. Runtime and forecasting advance this same state machine.
    public sealed class ProductionSimulation
    {
        public const long TicksPerSecond = 1000;
        public const long Never = long.MaxValue;
        public enum NodeKind { Source, Processor, Pickup }
        public enum TripPhase { Idle, Approach, Pickup, Travel, Placement }
        public enum EventKind { ConsumeStock, Produce, Deliver, Transfer }

        public sealed class Input
        {
            public int Item, Amount = 1, Capacity = 4;
        }

        public sealed class Node
        {
            public string Key;
            public NodeKind Kind;
            public int OutputItem = -1, OutputCapacity = 4, SharedCapacity;
            public int StockItem = -1;
            public long CycleTicks = 4000, SourcePeriodTicks = 2000;
            public int[] SourceItems = Array.Empty<int>();
            public Input[] Inputs = Array.Empty<Input>();
        }

        public sealed class Edge
        {
            public int From, To, Item, Flow;
        }

        public sealed class Worker
        {
            public int Flow, Capacity = 4, Origin;
            public long PickupTicks = 500, PlacementTicks = 500;
            public long TransportTicksPerItem = 1000;
            // Includes the worker's movement speed. A negative entry is unreachable.
            public long[,] TravelTicks;
        }

        public sealed class Definition
        {
            public Node[] Nodes = Array.Empty<Node>();
            public Edge[] Edges = Array.Empty<Edge>();
            public Worker[] Workers = Array.Empty<Worker>();
            public int ItemCount, FlowCount;
        }

        public sealed class Buffer
        {
            public int[] Inputs, ReservedInputs;
            public int Output, ReservedOutput, Processing;
            public long FinishAt = Never, CreditTicks, UpdatedAt;
            public int ReservedCredits;
            public Buffer(int items)
            {
                Inputs = new int[items];
                ReservedInputs = new int[items];
            }
            public Buffer Copy()
            {
                var copy = (Buffer)MemberwiseClone();
                copy.Inputs = (int[])Inputs.Clone();
                copy.ReservedInputs = (int[])ReservedInputs.Clone();
                return copy;
            }
        }

        public sealed class Trip
        {
            public TripPhase Phase;
            public int Location, Source = -1, Destination = -1, Item = -1, Units;
            public int CargoItem = -1, CargoUnits, CargoSource = -1;
            public long PhaseStarted, DueAt = Never, ReadyAt;
            public Trip Copy() => (Trip)MemberwiseClone();
        }

        public struct SimulationEvent
        {
            public EventKind Kind;
            public int Node, Item, Units, Worker, Flow;
            public long At;
        }

        public readonly Definition Plan;
        public readonly Buffer[] Buffers;
        public readonly Trip[] Trips;
        public readonly int[] Stock, ReservedStock;
        public readonly long[] Delivered;
        public readonly long[,] DeliveredByItem;
        public readonly List<SimulationEvent> Events = new List<SimulationEvent>();
        public long Now { get; private set; }
        public bool UnlimitedStock, DrainPickup;
        public bool EventLimitReached { get; private set; }
        public int EventsProcessed { get; private set; }

        public ProductionSimulation(Definition plan)
        {
            Plan = plan;
            Buffers = new Buffer[plan.Nodes.Length];
            for (int i = 0; i < Buffers.Length; i++)
            {
                Buffers[i] = new Buffer(plan.ItemCount);
                if (plan.Nodes[i].Kind == NodeKind.Source)
                    Buffers[i].CreditTicks = plan.Nodes[i].SourcePeriodTicks != Never ? 4 * plan.Nodes[i].SourcePeriodTicks : 0;
            }
            Trips = new Trip[plan.Workers.Length];
            for (int i = 0; i < Trips.Length; i++)
                Trips[i] = new Trip { Location = plan.Workers[i].Origin };
            Stock = new int[plan.ItemCount];
            ReservedStock = new int[plan.ItemCount];
            Delivered = new long[plan.FlowCount];
            DeliveredByItem = new long[plan.FlowCount, plan.ItemCount];
        }

        ProductionSimulation(ProductionSimulation other)
        {
            Plan = other.Plan; // Definitions are immutable after construction.
            Now = other.Now;
            UnlimitedStock = other.UnlimitedStock;
            DrainPickup = other.DrainPickup;
            Buffers = new Buffer[other.Buffers.Length];
            for (int i = 0; i < Buffers.Length; i++) Buffers[i] = other.Buffers[i].Copy();
            Trips = new Trip[other.Trips.Length];
            for (int i = 0; i < Trips.Length; i++) Trips[i] = other.Trips[i].Copy();
            Stock = (int[])other.Stock.Clone();
            ReservedStock = (int[])other.ReservedStock.Clone();
            Delivered = (long[])other.Delivered.Clone();
            DeliveredByItem = (long[,])other.DeliveredByItem.Clone();
        }

        public ProductionSimulation Copy() => new ProductionSimulation(this);
        public static long Seconds(double seconds) => Math.Max(1, (long)Math.Ceiling(seconds * TicksPerSecond - 1e-8));

        public int AvailableOutput(int station, int item)
        {
            Node node = Plan.Nodes[station];
            Buffer buffer = Buffers[station];
            if (node.Kind == NodeKind.Source)
            {
                if (Array.IndexOf(node.SourceItems, item) < 0 || node.SourcePeriodTicks == Never) return 0;
                int credits = (int)(buffer.CreditTicks / node.SourcePeriodTicks) - buffer.ReservedCredits;
                int stock = UnlimitedStock ? int.MaxValue : Math.Max(0, Stock[item] - ReservedStock[item]);
                return Math.Max(0, Math.Min(credits, stock));
            }
            return node.OutputItem == item ? Math.Max(0, buffer.Output - buffer.ReservedOutput) : 0;
        }

        public int FreeInput(int station, int item)
        {
            Node node = Plan.Nodes[station];
            Buffer buffer = Buffers[station];
            if (node.Kind == NodeKind.Pickup)
                return DrainPickup ? int.MaxValue : Math.Max(0, node.OutputCapacity - InputTotal(buffer));
            foreach (Input input in node.Inputs)
                if (input.Item == item)
                {
                    int free = input.Capacity - buffer.Inputs[item] - buffer.ReservedInputs[item];
                    if (node.SharedCapacity > 0)
                    {
                        int leaveForOtherIngredients = 0;
                        foreach (Input other in node.Inputs)
                            if (other.Item != item)
                                leaveForOtherIngredients += Math.Max(0, other.Amount
                                    - buffer.Inputs[other.Item] - buffer.ReservedInputs[other.Item]);
                        free = Math.Min(free, node.SharedCapacity - InputTotal(buffer)
                            - buffer.Processing - buffer.Output - leaveForOtherIngredients);
                    }
                    return Math.Max(0, free);
                }
            return 0;
        }

        static int InputTotal(Buffer buffer)
        {
            int total = 0;
            for (int i = 0; i < buffer.Inputs.Length; i++)
                total += buffer.Inputs[i] + buffer.ReservedInputs[i];
            return total;
        }

        void Emit(EventKind kind, int node, int item, int units, int worker = -1, int flow = -1)
        {
            Events.Add(new SimulationEvent { Kind = kind, Node = node, Item = item,
                Units = units, Worker = worker, Flow = flow, At = Now });
        }

        // Half-open forecast window: advance to the endpoint, including completions
        // at that endpoint. Dispatch at the endpoint is identical in live and replay.
        public void AdvanceTo(long target, int eventLimit = 1000000)
        {
            if (target < Now) throw new ArgumentOutOfRangeException(nameof(target));
            Events.Clear();
            EventsProcessed = 0;
            EventLimitReached = false;
            while (true)
            {
                Settle();
                if (Now == target) return;
                long next = NextEventAt();
                if (next <= Now) throw new InvalidOperationException("Production event did not advance the clock.");
                long destination = Math.Min(target, next);
                AdvanceCredits(destination);
                Now = destination;
                if (next > target) return;
                if (++EventsProcessed > eventLimit)
                {
                    EventLimitReached = true;
                    return;
                }
            }
        }

        void AdvanceCredits(long target)
        {
            for (int i = 0; i < Buffers.Length; i++)
            {
                Node node = Plan.Nodes[i];
                Buffer b = Buffers[i];
                if (node.Kind == NodeKind.Source && node.SourcePeriodTicks != Never)
                    b.CreditTicks = Math.Min(4 * node.SourcePeriodTicks, b.CreditTicks + target - b.UpdatedAt);
                b.UpdatedAt = target;
            }
        }

        void Settle()
        {
            // Stable ordering is part of the scheduling contract: station
            // completions, trip completions, station starts, then idle workers.
            for (int i = 0; i < Buffers.Length; i++)
            {
                Buffer b = Buffers[i];
                Node n = Plan.Nodes[i];
                if (b.Processing <= 0 || b.FinishAt > Now) continue;
                b.Processing--;
                b.Output++;
                b.FinishAt = b.Processing > 0 ? Now + n.CycleTicks : Never;
                Emit(EventKind.Produce, i, n.OutputItem, 1);
            }
            for (int i = 0; i < Trips.Length; i++)
                if (Trips[i].Phase != TripPhase.Idle && Trips[i].DueAt <= Now) CompletePhase(i);
            for (int i = 0; i < Buffers.Length; i++) StartProcessor(i);
            for (int i = 0; i < Trips.Length; i++)
                if (Trips[i].Phase == TripPhase.Idle) Dispatch(i);
        }

        void StartProcessor(int station)
        {
            Node n = Plan.Nodes[station];
            Buffer b = Buffers[station];
            if (n.Kind != NodeKind.Processor || n.OutputItem < 0 || n.CycleTicks == Never
                || b.Processing > 0 || b.Output >= n.OutputCapacity) return;
            foreach (Input input in n.Inputs)
                if (b.Inputs[input.Item] < input.Amount) return;
            if (n.StockItem >= 0 && !UnlimitedStock && Stock[n.StockItem] - ReservedStock[n.StockItem] <= 0) return;
            foreach (Input input in n.Inputs) b.Inputs[input.Item] -= input.Amount;
            if (n.StockItem >= 0)
            {
                if (!UnlimitedStock) Stock[n.StockItem]--;
                Emit(EventKind.ConsumeStock, station, n.StockItem, 1);
            }
            b.Processing = 1;
            b.FinishAt = Now + n.CycleTicks;
        }

        long NextEventAt()
        {
            long next = Never;
            foreach (Buffer buffer in Buffers) next = Math.Min(next, buffer.FinishAt);
            foreach (Trip trip in Trips)
            {
                next = Math.Min(next, trip.DueAt);
                if (trip.Phase == TripPhase.Idle && trip.ReadyAt > Now) next = Math.Min(next, trip.ReadyAt);
            }
            // Source credits accrue analytically. Only wake for a new credit if
            // an idle worker could actually use it, avoiding thousands of useless
            // forecast events for blocked, empty, or disconnected sources.
            foreach (Edge edge in Plan.Edges)
            {
                Node n = Plan.Nodes[edge.From];
                Buffer b = Buffers[edge.From];
                if (n.Kind != NodeKind.Source || n.SourcePeriodTicks == Never || FreeInput(edge.To, edge.Item) <= 0
                    || (!UnlimitedStock && Stock[edge.Item] <= ReservedStock[edge.Item])) continue;
                long until = (b.ReservedCredits + 1L) * n.SourcePeriodTicks - b.CreditTicks;
                if (until <= 0) continue;
                for (int wi = 0; wi < Plan.Workers.Length; wi++)
                {
                    Worker worker = Plan.Workers[wi];
                    Trip trip = Trips[wi];
                    if (worker.Flow != edge.Flow || trip.Phase != TripPhase.Idle || trip.ReadyAt > Now
                        || trip.CargoUnits > 0 || worker.TravelTicks[trip.Location, edge.From] < 0
                        || worker.TravelTicks[edge.From, edge.To] < 0) continue;
                    next = Math.Min(next, Now + until);
                    break;
                }
            }
            return next;
        }

        void Dispatch(int workerIndex)
        {
            Worker worker = Plan.Workers[workerIndex];
            Trip trip = Trips[workerIndex];
            if (trip.ReadyAt > Now && trip.CargoUnits == 0) return;
            Edge best = null;
            int bestUnits = 0;
            double bestScore = double.NegativeInfinity;
            foreach (Edge edge in Plan.Edges)
            {
                if (edge.Flow != worker.Flow) continue;
                bool cargo = trip.CargoUnits > 0;
                if (cargo && (edge.Item != trip.CargoItem || edge.From != trip.CargoSource)) continue;
                long approach = worker.TravelTicks[trip.Location, cargo ? edge.To : edge.From];
                long loaded = worker.TravelTicks[edge.From, edge.To];
                if (approach < 0 || (!cargo && loaded < 0)) continue;
                int units = Math.Min(cargo ? trip.CargoUnits : worker.Capacity,
                    Math.Min(cargo ? trip.CargoUnits : AvailableOutput(edge.From, edge.Item), FreeInput(edge.To, edge.Item)));
                if (units <= 0) continue;
                Node destination = Plan.Nodes[edge.To];
                Buffer receiver = Buffers[edge.To];
                // Finish product first, then the receiver with the least buffered
                // processing time. Distances and edge order break ties deterministically.
                double score = destination.Kind == NodeKind.Pickup ? 1e12 : 1e9;
                int perCycle = 1;
                foreach (Input input in destination.Inputs)
                    if (input.Item == edge.Item) perCycle = input.Amount;
                double bufferedSeconds = (receiver.Inputs[edge.Item] + receiver.ReservedInputs[edge.Item])
                    * (double)destination.CycleTicks / perCycle / TicksPerSecond;
                score -= bufferedSeconds * 100000;
                score += units * 100;
                score -= (approach + (cargo ? 0 : loaded)) / (double)TicksPerSecond;
                if (score <= bestScore) continue;
                bestScore = score;
                best = edge;
                bestUnits = units;
            }
            if (best == null) return;
            trip.Source = best.From;
            trip.Destination = best.To;
            trip.Item = best.Item;
            trip.Units = bestUnits;
            if (trip.CargoUnits == 0) trip.ReadyAt = Now + bestUnits * worker.TransportTicksPerItem;
            Buffers[best.To].ReservedInputs[best.Item] += bestUnits;
            if (trip.CargoUnits > 0)
                BeginPhase(trip, TripPhase.Travel, worker.TravelTicks[trip.Location, best.To]);
            else
            {
                Buffer source = Buffers[best.From];
                if (Plan.Nodes[best.From].Kind == NodeKind.Source)
                {
                    source.ReservedCredits += bestUnits;
                    ReservedStock[best.Item] += bestUnits;
                }
                else source.ReservedOutput += bestUnits;
                BeginPhase(trip, TripPhase.Approach, worker.TravelTicks[trip.Location, best.From]);
            }
        }

        void BeginPhase(Trip trip, TripPhase phase, long ticks)
        {
            trip.Phase = phase;
            trip.PhaseStarted = Now;
            trip.DueAt = Now + Math.Max(1, ticks);
        }

        void CompletePhase(int index)
        {
            Trip t = Trips[index];
            Worker w = Plan.Workers[index];
            switch (t.Phase)
            {
                case TripPhase.Approach:
                    t.Location = t.Source;
                    if (t.Units == 0) { t.Phase = TripPhase.Idle; t.DueAt = Never; }
                    else BeginPhase(t, TripPhase.Pickup, w.PickupTicks);
                    break;
                case TripPhase.Pickup:
                    Buffer source = Buffers[t.Source];
                    if (Plan.Nodes[t.Source].Kind == NodeKind.Source)
                    {
                        source.CreditTicks -= t.Units * Plan.Nodes[t.Source].SourcePeriodTicks;
                        source.ReservedCredits -= t.Units;
                        ReservedStock[t.Item] -= t.Units;
                        if (!UnlimitedStock) Stock[t.Item] -= t.Units;
                        Emit(EventKind.ConsumeStock, t.Source, t.Item, t.Units, index, w.Flow);
                    }
                    else
                    {
                        source.Output -= t.Units;
                        source.ReservedOutput -= t.Units;
                    }
                    t.CargoItem = t.Item;
                    t.CargoUnits = t.Units;
                    t.CargoSource = t.Source;
                    BeginPhase(t, TripPhase.Travel, w.TravelTicks[t.Source, t.Destination]);
                    break;
                case TripPhase.Travel:
                    t.Location = t.Destination;
                    BeginPhase(t, TripPhase.Placement, w.PlacementTicks);
                    break;
                case TripPhase.Placement:
                    Buffer destination = Buffers[t.Destination];
                    destination.ReservedInputs[t.Item] -= t.Units;
                    bool pickup = Plan.Nodes[t.Destination].Kind == NodeKind.Pickup;
                    if (!pickup || !DrainPickup) destination.Inputs[t.Item] += t.Units;
                    if (pickup)
                    {
                        Delivered[w.Flow] += t.Units;
                        DeliveredByItem[w.Flow, t.Item] += t.Units;
                        Emit(EventKind.Deliver, t.Destination, t.Item, t.Units, index, w.Flow);
                    }
                    else Emit(EventKind.Transfer, t.Destination, t.Item, t.Units, index, w.Flow);
                    t.CargoUnits -= t.Units;
                    if (t.CargoUnits == 0) { t.CargoItem = -1; t.CargoSource = -1; }
                    t.Phase = TripPhase.Idle;
                    t.DueAt = Never;
                    break;
            }
        }

        // A cashier or inventory edit can consume shared stock between frames.
        // Cancel only trips which have not picked up yet; never recreate cargo.
        public void ReconcileStockReservations()
        {
            for (int i = Trips.Length - 1; i >= 0; i--)
            {
                Trip trip = Trips[i];
                if ((trip.Phase != TripPhase.Approach && trip.Phase != TripPhase.Pickup)
                    || Plan.Nodes[trip.Source].Kind != NodeKind.Source
                    || Stock[trip.Item] >= ReservedStock[trip.Item]) continue;
                Buffer source = Buffers[trip.Source];
                source.ReservedCredits -= trip.Units;
                ReservedStock[trip.Item] -= trip.Units;
                Buffers[trip.Destination].ReservedInputs[trip.Item] -= trip.Units;
                trip.Units = 0;
                trip.ReadyAt = Now;
                if (trip.Phase == TripPhase.Pickup)
                {
                    trip.Phase = TripPhase.Idle;
                    trip.DueAt = Never;
                }
            }
        }

        public long[] Forecast(long duration, bool unlimitedStock = false, bool drainPickup = false)
        {
            ProductionSimulation future = Copy();
            future.UnlimitedStock = unlimitedStock;
            future.DrainPickup = drainPickup;
            future.AdvanceTo(Now + duration);
            if (future.EventLimitReached) throw new InvalidOperationException("Production forecast exceeded its event budget.");
            var totals = new long[Delivered.Length];
            for (int i = 0; i < totals.Length; i++) totals[i] = future.Delivered[i] - Delivered[i];
            return totals;
        }
    }
}
