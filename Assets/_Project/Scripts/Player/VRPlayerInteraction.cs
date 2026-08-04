using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// VR玩家交互控制器（射线交互部分）
    /// 精灵球抓取/投掷见 PokeBallThrowController
    /// </summary>
    public class VRPlayerInteraction : MonoBehaviour
    {
        private GameManager _gm;
        private UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor _rayInteractor;

        private void Start()
        {
            _gm = GameManager.Instance;
            _rayInteractor = FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>();
        }

        public void TryInteract()
        {
            if (_gm.IsInBattle || _gm.IsPaused || _rayInteractor == null) return;
            if (_rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
            {
                var interactable = hit.collider.GetComponent<IInteractable>();
                interactable?.OnInteract();
            }
        }
    }

    /// <summary>
    /// 可交互对象接口
    /// </summary>
    public interface IInteractable
    {
        void OnInteract();
        string GetInteractPrompt();
    }
}
