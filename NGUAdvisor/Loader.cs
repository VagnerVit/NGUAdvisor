using UnityEngine;
using Object = UnityEngine.Object;

namespace NGUAdvisor
{
    public class Loader
    {
        private static GameObject _load;
        private static Main _reference;

        public static void Init()
        {
            _load = new GameObject();
            _reference = _load.AddComponent<Main>();
            Object.DontDestroyOnLoad(_load);
        }

        public static void Unload()
        {
            _reference.Unload();
            _load.SetActive(false);
            Object.Destroy(_load);
        }
    }
}
