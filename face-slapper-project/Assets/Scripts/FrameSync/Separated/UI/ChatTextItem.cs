using UnityEngine;
using UnityEngine.UI;

namespace FaceSlapper.FrameSync.Separated.UI
{
    public class ChatTextItem : MonoBehaviour
    {
        private Text text;

        private void Awake()
        {
            text = GetComponentInChildren<Text>(true);
        }

        public void SetText(string txt)
        {
            if (text == null) text = GetComponentInChildren<Text>(true);
            if (text == null)
            {
                Debug.LogWarning("[ChatTextItem] Text component is missing");
                return;
            }
            text.supportRichText = false;
            text.text = txt;
            LayoutElement layout = GetComponent<LayoutElement>();
            if (layout != null)
                layout.preferredHeight = Mathf.Max(24f, text.preferredHeight + 8f);
        }
    }
}
