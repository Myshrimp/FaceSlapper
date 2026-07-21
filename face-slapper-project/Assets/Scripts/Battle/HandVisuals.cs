using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 手部表现切换：平时显示"放松"手掌，持有武器时右手切换为"握持"手掌。
    /// 通过子物体路径查找，无需手动拖拽引用。
    /// </summary>
    public class HandVisuals : MonoBehaviour
    {
        private GameObject _relaxedR;
        private GameObject _gripR;
        private PickWeaponAbility _pickWeapon;
        private bool _gripping;

        private void Awake()
        {
            _pickWeapon = GetComponent<PickWeaponAbility>();
            Transform relaxedR = transform.Find("Hands/HandR/Relaxed");
            Transform gripR = transform.Find("Hands/HandR/Grip");
            _relaxedR = relaxedR != null ? relaxedR.gameObject : null;
            _gripR = gripR != null ? gripR.gameObject : null;
            Apply();
        }

        private void Update()
        {
            bool gripping = _pickWeapon != null && _pickWeapon.HeldWeapon != null;
            if (gripping == _gripping) return;
            _gripping = gripping;
            Apply();
        }

        private void Apply()
        {
            if (_relaxedR != null) _relaxedR.SetActive(!_gripping);
            if (_gripR != null) _gripR.SetActive(_gripping);
        }
    }
}
