using UnityEngine;

namespace LuluDungeon
{
    public class Wander : MonoBehaviour
    {
        public float xMin = 3f, xMax = 31f, zMin = 3f, zMax = 31f;
        public float speed = 1.5f, rotSpeed = 3f;
        public float idleMin = 1.5f, idleMax = 4f;

        private Animator _anim;
        private Monster _monster;
        private string _walkClip = "WalkFWD";
        private string _idleClip = "IdleNormal";
        private Vector3 _target;
        private float _timer;
        private bool _moving;
        private string _lastClip = "";

        void Start()
        {
            _anim = GetComponent<Animator>();
            _monster = GetComponent<Monster>();
            if (_anim != null && _anim.runtimeAnimatorController != null)
            {
                foreach (var c in _anim.runtimeAnimatorController.animationClips)
                {
                    string n = c.name.ToLower();
                    if (n.Contains("walk") && n.Contains("fwd")) _walkClip = c.name;
                    if (n.Contains("idle") && n.Contains("normal")) _idleClip = c.name;
                }
            }
            NewTarget();
        }

        void Update()
        {
            // 战斗中：停止游走和动画覆盖，交给战斗系统
            if (_monster != null && _monster.InBattle)
            {
                _moving = false;
                return;
            }

            if (!_moving)
            {
                _timer -= Time.deltaTime;
                PlayAnim(_idleClip);
                if (_timer <= 0) { NewTarget(); _moving = true; }
                return;
            }

            Vector3 d = _target - transform.position; d.y = 0;
            if (d.magnitude < 0.5f) { _moving = false; _timer = Random.Range(idleMin, idleMax); return; }

            if (!Physics.Raycast(transform.position + Vector3.up * 0.5f, d.normalized, 1f))
            {
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(d.normalized), rotSpeed * Time.deltaTime);
                transform.Translate(Vector3.forward * speed * Time.deltaTime, Space.Self);
            }
            else NewTarget();

            PlayAnim(_walkClip);
        }

        void PlayAnim(string clipName)
        {
            if (_anim == null || clipName == _lastClip) return;
            _anim.Play(clipName);
            _lastClip = clipName;
        }

        void NewTarget()
        {
            for (int i = 0; i < 20; i++)
            {
                float tx = Random.Range(xMin, xMax), tz = Random.Range(zMin, zMax);
                if (Vector3.Distance(new Vector3(tx, 0, tz),
                    new Vector3(transform.position.x, 0, transform.position.z)) > 3f)
                { _target = new Vector3(tx, transform.position.y, tz); return; }
            }
            _target = new Vector3(Random.Range(xMin, xMax), transform.position.y, Random.Range(zMin, zMax));
        }
    }
}
