// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

// The assertions every consumer runs, over the same scenarios the benchmarks time. Returning a string rather than
// throwing keeps this usable from a console Main, from a Blazor component and from anywhere else a consumer can be
// built. Written to C# 7.3, because the lowest consumer tier compiles it at that language version.

// ReSharper disable All

using System;
using System.Collections.Generic;

namespace DeepEquals.Downstream
{
    public static class Checks
    {
        /// <summary>Runs every assertion. Returns a line starting "OK" or one starting "FAIL".</summary>
        public static string Run()
        {
            var failures = new List<string>();
            try
            {
                GraphChecks(failures);
                ScenarioChecks(failures);
                DifferenceChecks(failures);
            }
            catch (Exception ex)
            {
                failures.Add(ex.GetType().FullName + ": " + ex.Message);
            }

            if (failures.Count > 0) return "FAIL " + string.Join(", ", failures.ToArray());

            return "OK " + Describe();
        }

        /// <summary>A short note on the runtime, so a passing log says which one it passed on.</summary>
        public static string Describe()
        {
            string runtime = Type.GetType("Mono.Runtime") != null ? "mono" : "coreclr-or-netfx";
            return "runtime=" + runtime + " pointer=" + (IntPtr.Size * 8) + "-bit records=" + (Records ? "on" : "off");
        }

        private static bool Records
        {
            get
            {
#if RECORDS
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>The representation rules, on the graph shape: private storage, cycles, kind, scale, negative zero, order.</summary>
        private static void GraphChecks(List<string> failures)
        {
            var a = Data.Graph(1);
            var b = Data.Graph(1);

            // A cycle must terminate, and two structurally identical graphs must agree.
            if (!DownstreamContext.GraphNode.Equals(a, b)) failures.Add("graph equal");
            if (DownstreamContext.GraphNode.GetHashCode(a) != DownstreamContext.GraphNode.GetHashCode(b)) failures.Add("graph hash");

            // Instance storage is compared, so a difference in a private field is visible.
            var otherSecret = new GraphNode("other") { Value = a.Value, Tags = a.Tags, Counts = a.Counts, Stamp = a.Stamp, Amount = a.Amount, Rate = a.Rate };
            otherSecret.Next = otherSecret;
            if (DownstreamContext.GraphNode.Equals(a, otherSecret)) failures.Add("private field");

            // Ordered collections compare element by element, strings by their UTF-16 code units.
            var longer = Data.Graph(1);
            longer.Tags[1] = "yy";
            if (DownstreamContext.GraphNode.Equals(a, longer)) failures.Add("ordered collection");

            var otherKind = Data.Graph(1);
            otherKind.Stamp = new DateTime(a.Stamp.Ticks, DateTimeKind.Local);
            if (DownstreamContext.GraphNode.Equals(a, otherKind)) failures.Add("DateTime kind");

            var otherScale = Data.Graph(1);
            otherScale.Amount = a.Amount * 1.0m;   // the same value at one more decimal place
            if (DownstreamContext.GraphNode.Equals(a, otherScale)) failures.Add("decimal scale");

            var negativeZero = Data.Graph(1);
            negativeZero.Rate = -0d;
            var positiveZero = Data.Graph(1);
            positiveZero.Rate = 0d;
            if (DownstreamContext.GraphNode.Equals(negativeZero, positiveZero)) failures.Add("negative zero");

            // Dictionaries are sets of pairs; enumeration order must not reach the result.
            var reordered = Data.Graph(1);
            var counts = new Dictionary<string, int>();
            var keys = new List<string>(a.Counts.Keys);
            keys.Reverse();
            foreach (var key in keys) counts[key] = a.Counts[key];
            reordered.Counts = counts;
            if (!DownstreamContext.GraphNode.Equals(a, reordered)) failures.Add("unordered dictionary");
            if (DownstreamContext.GraphNode.GetHashCode(a) != DownstreamContext.GraphNode.GetHashCode(reordered)) failures.Add("unordered dictionary hash");
        }

        /// <summary>Every benchmark scenario: the generated comparer and the built-in agree that the pair is equal, and the hashes match.</summary>
        private static void ScenarioChecks(List<string> failures)
        {
            foreach (var scenario in Scenarios.All())
            {
                if (!scenario.GeneratedEquals()) failures.Add(scenario.Name + ": generated equals");
                if (!scenario.BuiltInEquals()) failures.Add(scenario.Name + ": built-in equals");
                if (scenario.GeneratedHash() != scenario.GeneratedHashOfOther()) failures.Add(scenario.Name + ": hash");
                if (scenario.GeneratedHash() != scenario.GeneratedHash()) failures.Add(scenario.Name + ": hash unstable");
            }
        }

        /// <summary>One difference per scenario shape, each of which the generated comparer must see.</summary>
        private static void DifferenceChecks(List<string> failures)
        {
            var customer = Data.Customer(1);
            var otherCustomer = Data.Customer(1);
            otherCustomer.Key = new Guid(2, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8);
            if (DownstreamContext.Customer.Equals(customer, otherCustomer)) failures.Add("customer difference");

            var order = Data.Order(2);
            var otherOrder = Data.Order(2);
            var lines = new List<OrderLine>(otherOrder.Lines);
            lines[7] = new OrderLine { Sku = lines[7].Sku, Price = lines[7].Price, Quantity = lines[7].Quantity, Position = lines[7].Position, Note = "changed", When = lines[7].When };
            otherOrder.Lines = lines;
            if (DownstreamContext.Order.Equals(order, otherOrder)) failures.Add("order line difference");

            var reorderedOrder = Data.Order(2);
            reorderedOrder.Tags = new HashSet<string> { "d", "c", "b", "a" };
            reorderedOrder.Attributes = new Dictionary<string, decimal>();
            foreach (var pair in order.Attributes) reorderedOrder.Attributes[pair.Key] = pair.Value;
            if (!DownstreamContext.Order.Equals(order, reorderedOrder)) failures.Add("order unordered members");

            var tree = Data.Tree(6, 3, 8);
            var otherTree = Data.Tree(6, 3, 8);
            otherTree.Children[3].Children[5].Children[7].Value++;
            if (DownstreamContext.TreeNode.Equals(tree, otherTree)) failures.Add("tree leaf difference");

            var map = Data.StringMap(4, 100);
            var otherMap = Data.StringMap(4, 100);
            otherMap["key50"] = otherMap["key50"] + 1;
            if (DownstreamContext.DictionaryOfStringAndDecimal.Equals(map, otherMap)) failures.Add("map value difference");

            var mapOtherComparer = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in map) mapOtherComparer[pair.Key] = pair.Value;
            if (!DownstreamContext.DictionaryOfStringAndDecimal.Equals(map, mapOtherComparer)) failures.Add("map other comparer");

            Shape square = Data.Square(7);
            Shape circle = new Circle { Name = square.Name, Radius = 1 };
            if (DownstreamContext.Shape.Equals(square, circle)) failures.Add("shape type difference");

            var payload = Data.Payload(8);
            var otherPayload = Data.Payload(8);
            otherPayload.Values["text"] = "other";
            if (DownstreamContext.Payload.Equals(payload, otherPayload)) failures.Add("payload entry difference");
            var otherPayloadType = Data.Payload(8);
            otherPayloadType.Values["int"] = (long)(int)otherPayloadType.Values["int"];
            if (DownstreamContext.Payload.Equals(payload, otherPayloadType)) failures.Add("payload runtime type difference");

#if RECORDS
            var invoice = Data.Invoice(11);
            var otherInvoice = invoice with { BillTo = invoice.BillTo with { Zip = invoice.BillTo.Zip + 1 } };
            if (DownstreamContext.Invoice.Equals(invoice, otherInvoice)) failures.Add("record nested difference");
            if (!otherInvoice.Equals(invoice with { BillTo = otherInvoice.BillTo })) failures.Add("record built-in sanity");

            var person = Data.Person(12);
            var noBalance = person with { Balance = null };
            if (DownstreamContext.Person.Equals(person, noBalance)) failures.Add("record nullable difference");

            var money = Data.Money(9);
            var otherMoney = money with { Currency = "USD" };
            if (DownstreamContext.Money.Equals(money, otherMoney)) failures.Add("record struct difference");
#endif
        }
    }
}
