using System;
using System.Collections.Generic;
using UnityEngine;
public static class GameSettings
{
    public const int TicksPerSec = 30;
    public const float TickFreq = 1f / TicksPerSec;
    public static float Ticks2Sec(int ticks)
    {
        return ticks * TickFreq;
    }
    public static int Sec2Ticks(float sec)
    {
        return (int)Mathf.Round(sec * TicksPerSec);
    }
}
[Serializable]
public class Timeline
{
    public Timeline(string id, int start, int length)
    {
        Id = id;
        StartPoint = start;
        Length = length;
        AttachedMap = new Dictionary<int, List<Timeline>>();
    }
    public int StartPoint;
    public int CurTick;
    public int CurPredictedTick;
    public int Length;
    public bool Started;
    public bool Stopped;
    public bool Ended;
    public string Id;
    public int TicksPerClear = 30; //每多少帧清理一次Timeline
    public Action<Timeline> OnAttached;
    public Dictionary<int, List<Timeline>> AttachedMap;
    public Timeline ParentTimeline;
    public void StartTick()
    {
        Started = true;
    }

    public void Attach(int tick, Timeline timeline)
    {
        AttachedMap[tick].Add(timeline);
        timeline.ParentTimeline = this;
    }

    public void Tick(int targetTick)
    {
        if(targetTick > CurTick)
        {
            Debug.Log("Lost some ticks! from " +  CurTick + " to " + targetTick);
        }
        OnTick();
        OnSubTimelinesTick(CurTick);
        CurTick += 1;
        if(CurTick % TicksPerClear == 0)
        {
            Clear();
        }
        // Length <= 0 表示无限时长，不判定结束。
        if(Length > 0 && CurTick > StartPoint + Length)
        {
            Ended = true;
            OnExit();
        }
    }

    public void PredictedTick()
    {
        OnTick();
        OnSubTimelinesTick(CurPredictedTick);
        CurPredictedTick += 1;
    }

    private void OnSubTimelinesTick(int tick)
    {
        if (AttachedMap.ContainsKey(tick) && AttachedMap[tick].Count > 0)
        {
            foreach (var timeline in AttachedMap[tick])
            {
                timeline.OnTick();
            }
        }
    }

    private void Clear()
    {
        foreach(var timelines in AttachedMap.Values)
        {
            for(int i=0; i<timelines.Count; i++)
            {
                if (timelines[i].Ended)
                {
                    timelines[i] = null;
                }
            }
        }
    }
    public virtual void OnTick() { }
    public virtual void OnExit() { }
}
