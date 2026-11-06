using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SaveData
{
    public int area;
    public int round;
    public int medal;
    public int point;

    // 所持レコード
    public List<string> ownedRecords = new();

    //装備レコード
    public string slot;
}
