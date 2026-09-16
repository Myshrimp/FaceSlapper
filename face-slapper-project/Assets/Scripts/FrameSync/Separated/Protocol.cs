using Newtonsoft.Json;

namespace FaceSlapper.FrameSync.Separated
{
    public class Protocol
    {
        public const int MaxDataLength = 8192;

        public static bool TryReadData(string json, out DataMsg data)
        {
            data = default;
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxDataLength) return false;
            try
            {
                data = JsonConvert.DeserializeObject<DataMsg>(json);
                return !string.IsNullOrWhiteSpace(data.head) && !string.IsNullOrWhiteSpace(data.body);
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    public struct EventBase
    {
        public EventBase(DataMsg data)
        {
            head = data.head;
            body = data.body;
        }
        public string head;
        public string body;
    }

    public struct ServerChatEvent
    {
        public ServerMain source;
        public DataMsg data;
        public int senderClientId;

        public ServerChatEvent(ServerMain source, DataMsg data, int senderClientId)
        {
            this.source = source;
            this.data = data;
            this.senderClientId = senderClientId;
        }
    }

    public struct ChatEvent
    {
        public ChatEvent(DataMsg data)
        {
            baseEvent = new EventBase(data);
        }
        public EventBase baseEvent;
    }
    
    public struct StartGameEvent
    {
        public StartGameEvent(DataMsg data)
        {
            baseEvent = new EventBase(data);
        }
        public EventBase baseEvent;
    }
}