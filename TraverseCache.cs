#nullable enable

using HarmonyLib;

namespace ATFWyvernMod
{
    /// <summary>
    /// A helper class to cache Harmony <see cref="Traverse"/> instances for repeatedly accessing a field on an object.
    /// This improves performance by avoiding the overhead of creating a new Traverse instance every time the value is needed for the same object.
    /// </summary>
    /// <typeparam name="TObject">The type of the object containing the field.</typeparam>
    /// <typeparam name="TValue">The type of the value stored in the field.</typeparam>
    public class TraverseCache<TObject, TValue> where TObject : class
    {
        private readonly string fieldName;
        private TObject? cachedObject;
        private Traverse? traverse;

        public TraverseCache(string fieldName)
        {
            this.fieldName = fieldName;
        }

        /// <summary>
        /// Retrieves the value of the field for the specified object instance.
        /// Updates the cache if the object instance has changed since the last call.
        /// </summary>
        /// <param name="currentObject">The object instance to retrieve the value from.</param>
        /// <param name="silent">If true, suppresses the log message when the cache is updated.</param>
        /// <returns>The value of the field, or null if not found.</returns>
        public TValue? GetValue(TObject currentObject, bool silent = false)
        {
            if (currentObject == null) return default(TValue);

            if (traverse == null || cachedObject != currentObject)
            {
                cachedObject = currentObject;
                traverse = Traverse.Create(currentObject).Field(fieldName);
                if (!silent)
                {
                    Plugin.Log.LogDebug($"[TraverseCache<{typeof(TObject).Name}, {typeof(TValue).Name}>] Cached field '{fieldName}' for object of type '{typeof(TObject).Name}'.");
                }
            }
            
            try
            {
                return traverse.GetValue<TValue>();
            }
            catch
            {
                return default(TValue);
            }
        }

        /// <summary>
        /// Sets the value of the field for the specified object instance.
        /// </summary>
        /// <param name="currentObject">The object instance to set the value on.</param>
        /// <param name="value">The value to set.</param>
        /// <param name="silent">If true, suppresses the log message when the cache is updated.</param>
        public void SetValue(TObject currentObject, TValue value, bool silent = false)
        {
            if (currentObject == null) return;

            if (traverse == null || cachedObject != currentObject)
            {
                cachedObject = currentObject;
                traverse = Traverse.Create(currentObject).Field(fieldName);
                if (!silent)
                {
                    Plugin.Log.LogDebug($"[TraverseCache<{typeof(TObject).Name}, {typeof(TValue).Name}>] Cached field '{fieldName}' for object of type '{typeof(TObject).Name}'.");
                }
            }
            
            try
            {
                traverse.SetValue(value);
            }
            catch (System.Exception ex)
            {
                if (!silent)
                {
                    Plugin.Log.LogWarning($"[TraverseCache] Error setting field '{fieldName}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Clears the cached object and traverse instance.
        /// </summary>
        public void Reset()
        {
            cachedObject = null;
            traverse = null;
        }
    }
}
