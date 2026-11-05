using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    // Temporary T006 check that Unity can call Game.Rules. The numbers are the GDD 8.3 example,
    // not balance data, and this class goes away once the real HUD reads from the rules.
    public sealed class RulesProbe : MonoBehaviour
    {
        private string message;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            new GameObject(nameof(RulesProbe)).AddComponent<RulesProbe>();
        }

        public static string Describe()
        {
            decimal damage = SkillDamage.BeforeDefense(40m, 100m, 1.2m, 10m, 5);
            return $"Game.Rules: {damage:0}";
        }

        public static void LogResult()
        {
            Debug.Log(Describe());
        }

        private void Awake()
        {
            message = Describe();
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(20f, 20f, 400f, 40f), message);
        }
    }
}
