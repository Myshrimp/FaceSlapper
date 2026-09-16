using FaceSlapper.Core;
using FaceSlapper.FrameSync.Separated.UI;
using FaceSlapper.Networking;
using Newtonsoft.Json;
using UnityEngine;

namespace FaceSlapper.FrameSync.Separated.Services
{
    public class ChatService : ServiceBase
    {
        public const int MaxMessageLength = 256;
        private ChatView _chatView;

        public override void OnAwake()
        {
            base.OnAwake();
            if (isServer)
                EventBus.Subscribe<ServerChatEvent>(OnServerReceiveChatEvent);
            else
                EventBus.Subscribe<ChatEvent>(OnClientReceiveChatEvent);
        }

        public override void OnDestroy()
        {
            EventBus.Unsubscribe<ServerChatEvent>(OnServerReceiveChatEvent);
            EventBus.Unsubscribe<ChatEvent>(OnClientReceiveChatEvent);
            _chatView = null;
            base.OnDestroy();
        }

        public bool TrySendMessage(string message, out string error)
        {
            error = null;
            if (isServer || gameMain == null || gameMain.ServerMain == null ||
                !Net.IsClient || Net.LocalClientId < 0 || !gameMain.ServerMain.IsClientReady)
            {
                error = "\u5c1a\u672a\u8fde\u63a5\u5230\u623f\u95f4";
                return false;
            }
            message = message?.Trim();
            if (string.IsNullOrEmpty(message))
            {
                error = "\u8bf7\u8f93\u5165\u804a\u5929\u5185\u5bb9";
                return false;
            }
            if (message.Length > MaxMessageLength)
            {
                error = $"\u804a\u5929\u5185\u5bb9\u4e0d\u80fd\u8d85\u8fc7 {MaxMessageLength} \u4e2a\u5b57\u7b26";
                return false;
            }
            DataMsg data = CreateMessage(Net.LocalClientId, message);
            gameMain.ServerMain.RequestClientSendData(JsonConvert.SerializeObject(data));
            return true;
        }

        public static bool TryReadMessage(DataMsg data, out MsgHead head, out ChatBody body)
        {
            head = default;
            body = default;
            if (string.IsNullOrWhiteSpace(data.head) || string.IsNullOrWhiteSpace(data.body)) return false;
            try
            {
                head = JsonConvert.DeserializeObject<MsgHead>(data.head);
                body = JsonConvert.DeserializeObject<ChatBody>(data.body);
                body.message = body.message?.Trim();
                return head.module == "Chat" && head.eventType == "Message" && head.isBroadcast &&
                    !string.IsNullOrEmpty(body.message) && body.message.Length <= MaxMessageLength;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static DataMsg CreateMessage(int senderClientId, string message)
        {
            var head = new MsgHead
            {
                fromPlayer = senderClientId,
                isBroadcast = true,
                module = "Chat",
                eventType = "Message"
            };
            return new DataMsg(JsonConvert.SerializeObject(head),
                JsonConvert.SerializeObject(new ChatBody { message = message }));
        }

        private void OnClientReceiveChatEvent(ChatEvent chatEvent)
        {
            if (!Net.IsClient || !TryReadMessage(new DataMsg(chatEvent.baseEvent.head, chatEvent.baseEvent.body),
                out MsgHead head, out ChatBody body)) return;
            if (!_chatView)
                _chatView = GameObject.FindObjectOfType<ChatView>();
            if (_chatView)
                _chatView.AddMsgItem($"[User {head.fromPlayer}]:{body.message}");
        }

        private void OnServerReceiveChatEvent(ServerChatEvent chatEvent)
        {
            if (chatEvent.source != serverMain || !serverMain.IsServer ||
                !TryReadMessage(chatEvent.data, out _, out ChatBody body)) return;
            serverMain.BroadcastData(CreateMessage(chatEvent.senderClientId, body.message));
        }
    }

    public struct ChatBody
    {
        public string message;
    }
}
