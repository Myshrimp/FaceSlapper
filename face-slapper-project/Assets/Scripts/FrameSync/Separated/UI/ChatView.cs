using System;
using System.Collections.Generic;
using UnityEngine;

namespace FaceSlapper.FrameSync.Separated.UI
{
    public class ChatView : MonoBehaviour
    {
        [SerializeField]
        private GameObject msgItemPrefab;
        private Transform msgItemRoot;
        private List<GameObject> msgItems;

        private void Start()
        {
            msgItems = new List<GameObject>();
            //msgItemRoot = transform.Find("msgRoot").transform;
        }

        private void OnDestroy()
        {
            foreach (var obj in msgItems)
            {
                Destroy(obj);
            }
        }

        public void AddMsgItem(string msg)
        {
            var item = Instantiate(msgItemPrefab, msgItemRoot);
            item.GetComponent<ChatTextItem>().SetText(msg);
            msgItems.Add(item);
        }
    }
}