using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ClaudeLab
{
    /// <summary>Finding and describing the objects spawn_object made. Shared by the tools that read or change them.</summary>
    public static class LabQuery
    {
        /// <summary>Every object in the lab, in spawn order.</summary>
        public static List<Transform> All(SpawnObjectTool spawner)
        {
            var list = new List<Transform>();
            foreach (Transform child in spawner.Container) list.Add(child);
            return list;
        }

        /// <summary>
        /// Objects matching a name: "ball #1" (exact, ignoring case, spaces and '#'), else a type like "ball"
        /// for every ball. Empty name = every object.
        /// </summary>
        public static List<Transform> Find(SpawnObjectTool spawner, string name)
        {
            var all = All(spawner);
            string query = Normalize(name);
            if (query.Length == 0) return all;
            var exact = all.Where(t => Normalize(t.name) == query).ToList();
            if (exact.Count > 0) return exact;
            return all.Where(t => TypeOf(t) == query).ToList();
        }

        /// <summary>"ball", "block", "pendulum" or "ramp", from names like "ball #3".</summary>
        public static string TypeOf(Transform t)
        {
            int hash = t.name.IndexOf(" #");
            return (hash > 0 ? t.name.Substring(0, hash) : t.name).ToLowerInvariant();
        }

        public static string NotFound(SpawnObjectTool spawner, string name)
        {
            var all = All(spawner);
            return all.Count == 0
                ? "The lab is empty."
                : $"No object called '{name}'. Objects in the lab: {string.Join(", ", all.Select(t => t.name))}.";
        }

        /// <summary>The body that moves: the object itself, or a pendulum's bob.</summary>
        public static Rigidbody MovingBody(Transform t)
        {
            if (t.TryGetComponent<Rigidbody>(out var body)) return body;
            var bob = t.Find("bob");
            return bob != null ? bob.GetComponent<Rigidbody>() : null;
        }

        public static string Normalize(string s) =>
            string.IsNullOrWhiteSpace(s) ? "" : new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }
}
