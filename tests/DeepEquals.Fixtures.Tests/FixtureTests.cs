// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using DeepEquals.SourceGeneration.Framework;
using FluentAssertions;
using Xunit;
// ReSharper disable ReturnValueOfPureMethodIsNotUsed

// ReSharper disable UseObjectOrCollectionInitializer

namespace DeepEquals.Fixtures
{
    public sealed class FixtureTests
    {
        private static Person MakePerson(string secret)
        {
            var p = new Person(secret);
            p.Name = "Ada";
            p.Age = 36;
            p.Score = 1.5;
            p.Id = new Guid("11111111-2222-3333-4444-555555555555");
            p.Born = new DateTime(1815, 12, 10, 0, 0, 0, DateTimeKind.Utc);
            p.Balance = 1.10m;
            p.Ticks = 1L << 40;
            return p;
        }

        [Fact]
        public void Person_compares_private_state_and_exact_representations()
        {
            var a = MakePerson("s");
            var b = MakePerson("s");
            FixtureContext.Person.Equals(a, b).Should().BeTrue();
            FixtureContext.Person.GetHashCode(a).Should().Be(FixtureContext.Person.GetHashCode(b));

            var c = MakePerson("other");
            FixtureContext.Person.Equals(a, c).Should().BeFalse("private fields are compared");

            var d = MakePerson("s");
            d.Balance = 1.1m;
            FixtureContext.Person.Equals(a, d).Should().BeFalse("decimal scale participates");

            var e = MakePerson("s");
            e.Born = new DateTime(a.Born.Ticks, DateTimeKind.Local);
            FixtureContext.Person.Equals(a, e).Should().BeFalse("DateTime kind participates");

            var f = MakePerson("s");
            f.Score = -0.0;
            var g = MakePerson("s");
            g.Score = 0.0;
            FixtureContext.Person.Equals(f, g).Should().BeFalse("floating point is bitwise");

            FixtureContext.Person.Equals(null, null).Should().BeTrue();
            FixtureContext.Person.Equals(a, null).Should().BeFalse();
            FixtureContext.Person.GetHashCode(null).Should().Be(0);
        }

        [Fact]
        public void Cycles_terminate_and_unrolled_cycles_are_equal()
        {
            var a = new Node { Value = 1 };
            a.Next = a;
            var b1 = new Node { Value = 1 };
            var b2 = new Node { Value = 1 };
            b1.Next = b2;
            b2.Next = b1;
            FixtureContext.Node.Equals(a, b1).Should().BeTrue();
            FixtureContext.Node.GetHashCode(a).Should().Be(FixtureContext.Node.GetHashCode(b1));

            var c = new Node { Value = 1, Children = new List<Node>(1) };
            c.Children.Add(c);
            var d = new Node { Value = 1, Children = new List<Node>(1) };
            d.Children.Add(d);
            FixtureContext.Node.Equals(c, d).Should().BeTrue("cycles through collections terminate");
            FixtureContext.Node.GetHashCode(c).Should().Be(FixtureContext.Node.GetHashCode(d));
        }

        [Fact]
        public void Long_chains_use_one_stack_frame()
        {
            var head1 = Chain(200_000);
            var head2 = Chain(200_000);
            FixtureContext.Node.Equals(head1, head2).Should().BeTrue();
            head2.Next.Next.Value = -1;
            FixtureContext.Node.Equals(head1, head2).Should().BeFalse();
        }

        private static Node Chain(int length)
        {
            var head = new Node { Value = 0 };
            var current = head;
            for (var i = 1; i < length; i++)
            {
                var next = new Node { Value = i };
                current.Next = next;
                current = next;
            }

            return head;
        }

        [Fact]
        public void Dispatch_uses_the_runtime_type()
        {
            var c1 = new Circle { Radius = 2, Name = "c" };
            var c2 = new Circle { Radius = 2, Name = "c" };
            var s1 = new Square { Side = 2, Name = "c" };
            var k1 = new Cube { Side = 2, Depth = 3, Name = "c" };
            var k2 = new Cube { Side = 2, Depth = 3, Name = "c" };

            FixtureContext.Shape.Equals(c1, c2).Should().BeTrue();
            FixtureContext.Shape.Equals(c1, s1).Should().BeFalse();
            FixtureContext.Square.Equals(k1, k2).Should().BeTrue("a derived instance behind its base uses the derived core");
            FixtureContext.Square.Equals(s1, k1).Should().BeFalse("runtime types differ");
            FixtureContext.Shape.GetHashCode(k1).Should().Be(FixtureContext.Square.GetHashCode(k2));

            Action unknown = () => FixtureContext.Shape.Equals(new Unregistered(), new Unregistered());
            unknown.Should().Throw<DeepEqualsUnknownTypeException>();

            FixtureContext.Object.Equals("a", "a").Should().BeTrue();
            FixtureContext.Object.Equals(1, 1).Should().BeTrue();
            FixtureContext.Object.Equals(1, 2).Should().BeFalse();
            FixtureContext.Object.Equals(1, "1").Should().BeFalse();
            FixtureContext.Object.Equals(new object(), new object()).Should().BeTrue();
            FixtureContext.Object.Equals(c1, c2).Should().BeTrue();
            FixtureContext.Object.GetHashCode(c1).Should().Be(FixtureContext.Object.GetHashCode(c2));
        }

        [Fact]
        public void Holder_covers_structs_nullables_enums_collections_and_products()
        {
            var a = MakeHolder();
            var b = MakeHolder();
            FixtureContext.Holder.Equals(a, b).Should().BeTrue();
            FixtureContext.Holder.GetHashCode(a).Should().Be(FixtureContext.Holder.GetHashCode(b));

            b.Ignored = 99;
            FixtureContext.Holder.Equals(a, b).Should().BeTrue("ignored members do not participate");

            b.Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Y", "x" };
            FixtureContext.Holder.Equals(a, b).Should().BeFalse("set elements are ordinal");
            b.Tags = new HashSet<string> { "y", "x" };
            FixtureContext.Holder.Equals(a, b).Should().BeTrue("sets are unordered");

            b.Words = new[] { "b", "a" };
            FixtureContext.Holder.Equals(a, b).Should().BeFalse("an IEnumerable view is ordered");
            b.Words = new List<string> { "a", "b" };
            FixtureContext.Holder.Equals(a, b).Should().BeTrue("mixed implementations of an ordered view compare by contents");

            b.Wide = (Wide)(1L << 40 | 1L);
            FixtureContext.Holder.Equals(a, b).Should().BeFalse("64-bit enums see every bit");
            b.Wide = Wide.High;

            b.Anything = "different";
            FixtureContext.Holder.Equals(a, b).Should().BeFalse();
            b.Anything = 42;
            FixtureContext.Holder.Equals(a, b).Should().BeTrue();

            b.Maybe = null;
            FixtureContext.Holder.Equals(a, b).Should().BeFalse();
        }

        private static Holder MakeHolder()
        {
            var n1 = new Node { Value = 1 };
            var n2 = new Node { Value = 2 };
            return new Holder
            {
                P = new Point { X = 1, Y = 2 },
                Maybe = new Point { X = 3, Y = 4 },
                Large = new Big { A = 1, B = 2, Tag = "t" },
                Colour = Colour.Green,
                Wide = Wide.High,
                Tags = new HashSet<string> { "x", "y" },
                Counts = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } },
                Values = new[] { 1, 2, 3 },
                Points = new[] { new Point { X = 1, Y = 1 } },
                Words = new[] { "a", "b" },
                Nodes = new HashSet<Node> { n1, n2 },
                Anything = 42,
                Shape = new Circle { Radius = 1, Name = "s" },
                Pair = new KeyValuePair<int, Point>(1, new Point { X = 5, Y = 6 }),
                Tuple = new ValueTuple<int, string>(1, "one"),
                RefTuple = new Tuple<int, Point>(2, new Point { X = 7, Y = 8 }),
            };
        }

        [Fact]
        public void Generic_declaring_types_read_private_fields()
        {
            var a = new Boxes { Int = new Box<int>(1), Text = new Box<string>("t") };
            var b = new Boxes { Int = new Box<int>(1), Text = new Box<string>("t") };
            FixtureContext.Boxes.Equals(a, b).Should().BeTrue();
            FixtureContext.Boxes.GetHashCode(a).Should().Be(FixtureContext.Boxes.GetHashCode(b));
            b.Text = new Box<string>("u");
            FixtureContext.Boxes.Equals(a, b).Should().BeFalse("the private field of the generic type is compared");
        }

        [Fact]
        public void Empty_type_and_generic_lookup()
        {
            FixtureContext.Empty.Equals(new Empty(), new Empty()).Should().BeTrue();
            FixtureContext.Empty.GetHashCode(new Empty()).Should().Be(1);
            FixtureContext.GetEqualityComparer<Person>().Should().BeSameAs(FixtureContext.Person);
            FixtureContext.GetEqualityComparer<Point?>().Should().NotBeNull();
            Action missing = () => FixtureContext.GetEqualityComparer<Uri>();
            missing.Should().Throw<DeepEqualsMissingComparerException>();
        }
    }
}
