using Newtonsoft.Json;

namespace FaceSlapper.FrameSync.Separated.Services
{
    public class ChatService : ServiceBase
    {
        public override void OnAwake()
        {
            base.OnAwake();
            
        }

        private void OnClientReceiveChatEvent(ChatEvent chatEvent)
        {
            ChatHead head = JsonConvert.DeserializeObject<ChatHead>(chatEvent.baseEvent.head);
            ChatBody body = JsonConvert.DeserializeObject<ChatBody>(chatEvent.baseEvent.body);
        }
    }

    public struct ChatHead
    {
        public int fromId;
        public int toId;
        public bool toGlobal;
    }
    
    public struct ChatBody
    {
        public string message;
    }
}