using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
public class TrashCounter : BaseCounter
{
public override void Interact(Player player)
    {
        if (player.HasKitchenObject())
        {
            player.GetKitchenObject().DestroySelf();
        }
    }
}
