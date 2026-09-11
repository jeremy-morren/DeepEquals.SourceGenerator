// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

// One scenario per shape the package claims to handle well: a pair of structurally equal values, the generated
// comparer over them, and the built-in or hand-written equality a developer would otherwise use. The smoke checks
// assert on every scenario; the benchmarks time every scenario, on every platform and in the browser. Written to C# 7.3.

// ReSharper disable All

using System;
using System.Collections.Generic;

namespace DeepEquals.Downstream
{
    /// <summary>Equal operands and the four operations over them. The delegates are called on the same data every time.</summary>
    public sealed class Scenario
    {
        public Scenario(string name, Func<bool> generatedEquals, Func<bool> builtInEquals, Func<int> generatedHash, Func<int> builtInHash, Func<int> generatedHashOfOther)
        {
            Name = name;
            GeneratedEquals = generatedEquals;
            BuiltInEquals = builtInEquals;
            GeneratedHash = generatedHash;
            BuiltInHash = builtInHash;
            GeneratedHashOfOther = generatedHashOfOther;
        }

        public string Name { get; }

        /// <summary>The generated comparer's Equals over the pair; true by construction.</summary>
        public Func<bool> GeneratedEquals { get; }

        /// <summary>The built-in or hand-written equality over the same pair; true by construction.</summary>
        public Func<bool> BuiltInEquals { get; }

        public Func<int> GeneratedHash { get; }

        public Func<int> BuiltInHash { get; }

        /// <summary>The generated hash of the other operand, which the checks compare against <see cref="GeneratedHash"/>.</summary>
        public Func<int> GeneratedHashOfOther { get; }

        /// <summary>The name is what a benchmark table shows for the parameter.</summary>
        public override string ToString() { return Name; }
    }

    public static class Scenarios
    {
        /// <summary>Every scenario, each over freshly built data.</summary>
        public static Scenario[] All()
        {
            var scenarios = new List<Scenario>();

            var customerA = Data.Customer(1);
            var customerB = Data.Customer(1);
            scenarios.Add(new Scenario("Customer (wide leaves)",
                () => DownstreamContext.Customer.Equals(customerA, customerB),
                () => BuiltIn.CustomerEquals(customerA, customerB),
                () => DownstreamContext.Customer.GetHashCode(customerA),
                () => BuiltIn.CustomerHash(customerA),
                () => DownstreamContext.Customer.GetHashCode(customerB)));

            var orderA = Data.Order(2);
            var orderB = Data.Order(2);
            scenarios.Add(new Scenario("Order (lists, maps, set, hierarchy)",
                () => DownstreamContext.Order.Equals(orderA, orderB),
                () => BuiltIn.OrderEquals(orderA, orderB),
                () => DownstreamContext.Order.GetHashCode(orderA),
                () => BuiltIn.OrderHash(orderA),
                () => DownstreamContext.Order.GetHashCode(orderB)));

            IReadOnlyList<OrderLine> linesA = Data.Lines(3, 100);
            IReadOnlyList<OrderLine> linesB = Data.Lines(3, 100);
            scenarios.Add(new Scenario("IReadOnlyList<OrderLine> x100",
                () => DownstreamContext.IReadOnlyListOfOrderLine.Equals(linesA, linesB),
                () => BuiltIn.LinesEqual(linesA, linesB),
                () => DownstreamContext.IReadOnlyListOfOrderLine.GetHashCode(linesA),
                () => BuiltIn.LinesHash(linesA),
                () => DownstreamContext.IReadOnlyListOfOrderLine.GetHashCode(linesB)));

            var stringMapA = Data.StringMap(4, 100);
            var stringMapB = Data.StringMap(4, 100);
            scenarios.Add(new Scenario("Dictionary<string,decimal> x100",
                () => DownstreamContext.DictionaryOfStringAndDecimal.Equals(stringMapA, stringMapB),
                () => BuiltIn.StringMapEquals(stringMapA, stringMapB),
                () => DownstreamContext.DictionaryOfStringAndDecimal.GetHashCode(stringMapA),
                () => BuiltIn.StringMapHash(stringMapA),
                () => DownstreamContext.DictionaryOfStringAndDecimal.GetHashCode(stringMapB)));

            var skuMapA = Data.SkuMap(5, 100);
            var skuMapB = Data.SkuMap(5, 100);
            scenarios.Add(new Scenario("Dictionary<SkuId,int> x100",
                () => DownstreamContext.DictionaryOfSkuIdAndInt32.Equals(skuMapA, skuMapB),
                () => BuiltIn.SkuMapEquals(skuMapA, skuMapB),
                () => DownstreamContext.DictionaryOfSkuIdAndInt32.GetHashCode(skuMapA),
                () => BuiltIn.SkuMapHash(skuMapA),
                () => DownstreamContext.DictionaryOfSkuIdAndInt32.GetHashCode(skuMapB)));

            var treeA = Data.Tree(6, 3, 8);
            var treeB = Data.Tree(6, 3, 8);
            scenarios.Add(new Scenario("TreeNode x585 (cyclic type)",
                () => DownstreamContext.TreeNode.Equals(treeA, treeB),
                () => BuiltIn.TreeEquals(treeA, treeB),
                () => DownstreamContext.TreeNode.GetHashCode(treeA),
                () => BuiltIn.TreeHash(treeA),
                () => DownstreamContext.TreeNode.GetHashCode(treeB)));

            Shape shapeA = Data.Square(7);
            Shape shapeB = Data.Square(7);
            scenarios.Add(new Scenario("Shape (hierarchy dispatch)",
                () => DownstreamContext.Shape.Equals(shapeA, shapeB),
                () => BuiltIn.ShapeEquals(shapeA, shapeB),
                () => DownstreamContext.Shape.GetHashCode(shapeA),
                () => BuiltIn.ShapeHash(shapeA),
                () => DownstreamContext.Shape.GetHashCode(shapeB)));

            var payloadA = Data.Payload(8);
            var payloadB = Data.Payload(8);
            scenarios.Add(new Scenario("Payload (object dispatch)",
                () => DownstreamContext.Payload.Equals(payloadA, payloadB),
                () => BuiltIn.PayloadEquals(payloadA, payloadB),
                () => DownstreamContext.Payload.GetHashCode(payloadA),
                () => BuiltIn.PayloadHash(payloadA),
                () => DownstreamContext.Payload.GetHashCode(payloadB)));

#if RECORDS
            var moneyA = Data.Money(9);
            var moneyB = Data.Money(9);
            scenarios.Add(new Scenario("record struct Money",
                () => DownstreamContext.Money.Equals(moneyA, moneyB),
                () => moneyA.Equals(moneyB),
                () => DownstreamContext.Money.GetHashCode(moneyA),
                () => moneyA.GetHashCode(),
                () => DownstreamContext.Money.GetHashCode(moneyB)));

            var pointA = Data.Point3(10);
            var pointB = Data.Point3(10);
            scenarios.Add(new Scenario("record struct Point3",
                () => DownstreamContext.Point3.Equals(pointA, pointB),
                () => pointA.Equals(pointB),
                () => DownstreamContext.Point3.GetHashCode(pointA),
                () => pointA.GetHashCode(),
                () => DownstreamContext.Point3.GetHashCode(pointB)));

            var invoiceA = Data.Invoice(11);
            var invoiceB = Data.Invoice(11);
            scenarios.Add(new Scenario("record Invoice (nested record + record struct)",
                () => DownstreamContext.Invoice.Equals(invoiceA, invoiceB),
                () => invoiceA.Equals(invoiceB),
                () => DownstreamContext.Invoice.GetHashCode(invoiceA),
                () => invoiceA.GetHashCode(),
                () => DownstreamContext.Invoice.GetHashCode(invoiceB)));

            var personA = Data.Person(12);
            var personB = Data.Person(12);
            scenarios.Add(new Scenario("record Person (nested + nullable record struct)",
                () => DownstreamContext.Person.Equals(personA, personB),
                () => personA.Equals(personB),
                () => DownstreamContext.Person.GetHashCode(personA),
                () => personA.GetHashCode(),
                () => DownstreamContext.Person.GetHashCode(personB)));
#endif

            return scenarios.ToArray();
        }
    }
}
