// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

// What a developer writes by hand, or what the compiler writes for a record: member-wise equality through the
// framework's own operators and default comparers, and the record-style hash combine. These are the built-ins the
// benchmarks measure the generated comparers against. They differ from the generated rules exactly where the
// documented representation rules do (decimal scale, DateTime kind, negative zero), so the benchmark data avoids
// those edges and the smoke checks assert that both agree on it. Written to C# 7.3.

// ReSharper disable All

using System;
using System.Collections.Generic;

namespace DeepEquals.Downstream
{
    public static class BuiltIn
    {
        /// <summary>The combine a record's synthesized GetHashCode uses, which needs nothing beyond netstandard2.0.</summary>
        public static int Combine<T>(int hash, T value)
        {
            unchecked
            {
                return hash * -1521134295 + EqualityComparer<T>.Default.GetHashCode(value);
            }
        }

        // ----- Customer -----------------------------------------------------------------------------------------------

        public static bool CustomerEquals(Customer a, Customer b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.Name == b.Name
                   && a.Email == b.Email
                   && a.Age == b.Age
                   && a.Id == b.Id
                   && a.Key == b.Key
                   && a.Created == b.Created
                   && a.Balance == b.Balance
                   && a.Score == b.Score;
        }

        public static int CustomerHash(Customer a)
        {
            if (a == null) return 0;
            var h = 0;
            h = Combine(h, a.Name);
            h = Combine(h, a.Email);
            h = Combine(h, a.Age);
            h = Combine(h, a.Id);
            h = Combine(h, a.Key);
            h = Combine(h, a.Created);
            h = Combine(h, a.Balance);
            h = Combine(h, a.Score);
            return h;
        }

        // ----- OrderLine, lists of them, and Order ----------------------------------------------------------------------

        public static bool LineEquals(OrderLine a, OrderLine b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.Sku.Equals(b.Sku)
                   && a.Price == b.Price
                   && a.Quantity == b.Quantity
                   && a.Position == b.Position
                   && a.Note == b.Note
                   && a.When == b.When;
        }

        public static int LineHash(OrderLine a)
        {
            if (a == null) return 0;
            var h = 0;
            h = Combine(h, a.Sku);
            h = Combine(h, a.Price);
            h = Combine(h, a.Quantity);
            h = Combine(h, a.Position);
            h = Combine(h, a.Note);
            h = Combine(h, a.When);
            return h;
        }

        public static bool LinesEqual(IReadOnlyList<OrderLine> a, IReadOnlyList<OrderLine> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++)
                if (!LineEquals(a[i], b[i])) return false;

            return true;
        }

        public static int LinesHash(IReadOnlyList<OrderLine> a)
        {
            if (a == null) return 0;
            var h = 0;
            for (var i = 0; i < a.Count; i++)
                h = Combine(h, LineHash(a[i]));

            return h;
        }

        public static bool DecimalsEqual(IReadOnlyList<decimal> a, IReadOnlyList<decimal> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;

            return true;
        }

        public static bool StringMapEquals(Dictionary<string, decimal> a, Dictionary<string, decimal> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            foreach (var pair in a)
            {
                decimal other;
                if (!b.TryGetValue(pair.Key, out other) || other != pair.Value) return false;
            }

            return true;
        }

        public static int StringMapHash(Dictionary<string, decimal> a)
        {
            if (a == null) return 0;
            var sum = 0;
            foreach (var pair in a)
                unchecked { sum += Combine(Combine(0, pair.Key), pair.Value); }

            return Combine(a.Count, sum);
        }

        public static bool SkuMapEquals(Dictionary<SkuId, int> a, Dictionary<SkuId, int> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            foreach (var pair in a)
            {
                int other;
                if (!b.TryGetValue(pair.Key, out other) || other != pair.Value) return false;
            }

            return true;
        }

        public static int SkuMapHash(Dictionary<SkuId, int> a)
        {
            if (a == null) return 0;
            var sum = 0;
            foreach (var pair in a)
                unchecked { sum += Combine(Combine(0, pair.Key), pair.Value); }

            return Combine(a.Count, sum);
        }

        public static bool ShapeEquals(Shape a, Shape b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.GetType() != b.GetType()) return false;
            if (a.Name != b.Name) return false;

            var circle = a as Circle;
            if (circle != null) return circle.Radius == ((Circle)b).Radius;

            var rect = a as Rect;
            if (rect != null)
            {
                var otherRect = (Rect)b;
                if (rect.Width != otherRect.Width || rect.Height != otherRect.Height) return false;
                var square = a as Square;
                return square == null || square.Extra == ((Square)b).Extra;
            }

            throw new InvalidOperationException("unknown shape " + a.GetType());
        }

        public static int ShapeHash(Shape a)
        {
            if (a == null) return 0;
            var h = Combine(0, a.Name);
            var circle = a as Circle;
            if (circle != null) return Combine(h, circle.Radius);

            var rect = (Rect)a;
            h = Combine(Combine(h, rect.Width), rect.Height);
            var square = a as Square;
            return square == null ? h : Combine(h, square.Extra);
        }

        public static bool OrderEquals(Order a, Order b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.GetType() != b.GetType()) return false;
            return a.Sku.Equals(b.Sku)
                   && a.Price == b.Price
                   && a.Quantity == b.Quantity
                   && a.Reference == b.Reference
                   && LinesEqual(a.Lines, b.Lines)
                   && DecimalsEqual(a.Weights, b.Weights)
                   && StringMapEquals(a.Attributes, b.Attributes)
                   && SkuMapEquals(a.Counts, b.Counts)
                   && (ReferenceEquals(a.Tags, b.Tags) || (a.Tags != null && b.Tags != null && a.Tags.SetEquals(b.Tags)))
                   && ShapeEquals(a.Shape, b.Shape);
        }

        public static int OrderHash(Order a)
        {
            if (a == null) return 0;
            var h = 0;
            h = Combine(h, a.Sku);
            h = Combine(h, a.Price);
            h = Combine(h, a.Quantity);
            h = Combine(h, a.Reference);
            h = Combine(h, LinesHash(a.Lines));
            var weights = 0;
            if (a.Weights != null)
                for (var i = 0; i < a.Weights.Count; i++)
                    weights = Combine(weights, a.Weights[i]);

            h = Combine(h, weights);
            h = Combine(h, StringMapHash(a.Attributes));
            h = Combine(h, SkuMapHash(a.Counts));
            var tags = 0;
            if (a.Tags != null)
                foreach (var tag in a.Tags)
                    unchecked { tags += Combine(0, tag); }

            h = Combine(h, tags);
            h = Combine(h, ShapeHash(a.Shape));
            return h;
        }

        // ----- TreeNode -----------------------------------------------------------------------------------------------

        public static bool TreeEquals(TreeNode a, TreeNode b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Value != b.Value) return false;
            if (ReferenceEquals(a.Children, b.Children)) return true;
            if (a.Children == null || b.Children == null || a.Children.Count != b.Children.Count) return false;
            for (var i = 0; i < a.Children.Count; i++)
                if (!TreeEquals(a.Children[i], b.Children[i])) return false;

            return true;
        }

        public static int TreeHash(TreeNode a)
        {
            if (a == null) return 0;
            var h = Combine(0, a.Value);
            if (a.Children != null)
                for (var i = 0; i < a.Children.Count; i++)
                    h = Combine(h, TreeHash(a.Children[i]));

            return h;
        }

        // ----- Payload --------------------------------------------------------------------------------------------------

        public static bool PayloadEquals(Payload a, Payload b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (!Equals(a.Single, b.Single)) return false;
            if (ReferenceEquals(a.Values, b.Values)) return true;
            if (a.Values == null || b.Values == null || a.Values.Count != b.Values.Count) return false;
            foreach (var pair in a.Values)
            {
                object other;
                if (!b.Values.TryGetValue(pair.Key, out other) || !Equals(pair.Value, other)) return false;
            }

            return true;
        }

        public static int PayloadHash(Payload a)
        {
            if (a == null) return 0;
            var h = Combine(0, a.Single);
            var sum = 0;
            if (a.Values != null)
                foreach (var pair in a.Values)
                    unchecked { sum += Combine(Combine(0, pair.Key), pair.Value); }

            return Combine(h, sum);
        }
    }
}
