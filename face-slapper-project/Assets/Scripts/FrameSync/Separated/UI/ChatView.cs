using System.Collections.Generic;
using FaceSlapper.FrameSync.Separated.Services;
using UnityEngine;
using UnityEngine.UI;

namespace FaceSlapper.FrameSync.Separated.UI
{
    public class ChatView : MonoBehaviour
    {
        [SerializeField]
        private GameObject msgItemPrefab;
        [SerializeField]
        private Transform msgItemRoot;
        [SerializeField]
        private InputField messageInput;
        [SerializeField]
        private Button sendButton;
        [SerializeField]
        private Text statusText;
        [SerializeField]
        private ScrollRect scrollRect;
        private readonly List<GameObject> msgItems = new List<GameObject>();
        private GameMain _gameMain;
        private const int MaxMessages = 100;

        private void Awake()
        {
            if (messageInput != null)
            {
                messageInput.characterLimit = ChatService.MaxMessageLength;
                messageInput.onEndEdit.AddListener(OnEndEdit);
            }
            if (sendButton != null)
                sendButton.onClick.AddListener(SendChatMessage);
        }

        private void OnDestroy()
        {
            if (messageInput != null)
                messageInput.onEndEdit.RemoveListener(OnEndEdit);
            if (sendButton != null)
                sendButton.onClick.RemoveListener(SendChatMessage);
            foreach (var obj in msgItems)
                if (obj != null) Destroy(obj);
        }

        private void OnEndEdit(string value)
        {
            if ((UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)) &&
                string.IsNullOrEmpty(UnityEngine.Input.compositionString))
                SendChatMessage();
        }

        public void SendChatMessage()
        {
            if (messageInput == null) return;
            if (_gameMain == null)
                _gameMain = GameObject.FindObjectOfType<GameMain>();
            string error = "\u5c1a\u672a\u8fde\u63a5\u5230\u623f\u95f4";
            if (_gameMain != null && _gameMain.Chat != null &&
                _gameMain.Chat.TrySendMessage(messageInput.text, out error))
            {
                messageInput.text = string.Empty;
                error = string.Empty;
            }
            if (statusText != null) statusText.text = error;
            messageInput.ActivateInputField();
        }

        public void AddMsgItem(string msg)
        {
            if (msgItemPrefab == null || msgItemRoot == null)
            {
                Debug.LogWarning("[ChatView] Message prefab or root is missing");
                return;
            }
            var item = Instantiate(msgItemPrefab, msgItemRoot);
            ChatTextItem textItem = item.GetComponent<ChatTextItem>();
            if (textItem == null)
            {
                Destroy(item);
                Debug.LogWarning("[ChatView] Message prefab requires ChatTextItem");
                return;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)msgItemRoot);
            textItem.SetText(msg);
            msgItems.Add(item);
            if (msgItems.Count > MaxMessages)
            {
                GameObject oldest = msgItems[0];
                msgItems.RemoveAt(0);
                oldest.SetActive(false);
                Destroy(oldest);
            }
            Canvas.ForceUpdateCanvases();
            if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
        }
    }
}
