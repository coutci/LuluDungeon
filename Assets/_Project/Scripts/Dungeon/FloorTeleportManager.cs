using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 楼层传送管理：只允许在当前层传送
    /// </summary>
    public class FloorTeleportManager : MonoBehaviour
    {
        public float floorSpacing = 6.4f;
        public int totalFloors = 1; // Start with 1, increase when floors are duplicated

        private UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea[] _floorTAs;
        private Transform _player;

        private void Start()
        {
            _floorTAs = new UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea[totalFloors];
            
            for (int i = 0; i < totalFloors; i++)
            {
                var floorGroup = transform.Find("Floor_" + i.ToString("D2"));
                if (floorGroup == null) continue;
                
                var floorObj = floorGroup.Find("Floor");
                if (floorObj != null)
                    _floorTAs[i] = floorObj.GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>();
            }

            var origin = GameObject.Find("XR Origin (XR Rig)");
            if (origin != null) _player = origin.transform;
        }

        private void Update()
        {
            if (_player == null) return;
            int floor = Mathf.FloorToInt(_player.position.y / floorSpacing);
            floor = Mathf.Clamp(floor, 0, totalFloors - 1);
            
            for (int i = 0; i < _floorTAs.Length; i++)
            {
                if (_floorTAs[i] != null)
                    _floorTAs[i].enabled = (i == floor);
            }
        }
    }
}
