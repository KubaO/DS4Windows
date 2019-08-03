// From: https://github.com/Athari/Alba.Framework/blob/master/Alba.Framework/Collections/Common/KeyValuePairExts.cs
// Commit 33cdaf7
// Public Domain

using System.Collections.Generic;

namespace Alba.Framework.Collections
{
    public static class KeyValuePairExts
    {
        public static KeyValuePair<TValue, TKey> Reverse<TKey, TValue>(this KeyValuePair<TKey, TValue> @this)
        {
            return new KeyValuePair<TValue, TKey>(@this.Value, @this.Key);
        }
    }
}