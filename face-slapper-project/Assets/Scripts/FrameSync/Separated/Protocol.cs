namespace FaceSlapper.FrameSync.Separated
{
    public class Protocol
    {
        
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