using UnityEngine;
using UnityEngine.UI;

namespace FaceSlapper.FrameSync.Separated.UI
{
    public class ChatTextItem : MonoBehaviour
    {
        private Text text;

        private void Start()
        {
            text = transform.Find("Text").GetComponent<Text>();
        }

        public void SetText(string txt)
        {
            text.text = txt;
        }
    }
}