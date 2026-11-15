using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
public class ContainerCounter : BaseCounter
{
    public event EventHandler OnPlayerGrabbedObject;
    // ★ v1.1: возврат ингредиента в родной контейнер. Подписчик — ContainerCounterVisual
    // (анимация крышки), вешать что-то ещё не обязательно.
    public event EventHandler OnPlayerReturnedObject;
    [SerializeField] private KitchenObjectSO kitchenObjectSO;


    public override void Interact(Player player){
        if(!player.HasKitchenObject()){
            KitchenObject.SpawnKitchenObject(kitchenObjectSO,player);
            OnPlayerGrabbedObject?.Invoke(this,EventArgs.Empty);
            return;
        }

        // ★ Руки заняты: если игрок держит ТОТ ЖЕ ингредиент, что выдаёт этот контейнер
        // (бузина в руках + контейнер с бузиной и т.п.) — кладём его обратно в контейнер.
        KitchenObject heldObject = player.GetKitchenObject();

        // Тарелка (даже пустая) контейнеру не «родная»: пустая возвращается в стопку
        // через PlatesCounter, с блюдом — через стол/мойку/мусорку. Контейнер её игнорирует.
        if (heldObject.TryGetPlate(out PlateKitchenObject _))
        {
            return;
        }

        if (heldObject.GetKitchenObjectSo() == kitchenObjectSO)
        {
            // Тот же ингредиент: убираем его из рук — предмет «падает» обратно в контейнер
            // (DestroySelf сам освободит holdPoint игрока).
            heldObject.DestroySelf();
            OnPlayerReturnedObject?.Invoke(this,EventArgs.Empty);
        }
        // Чужой предмет (другой ингредиент, нарезка, зелье и т.п.) контейнер не принимает —
        // поведение прежнее: ничего не происходит.
    }
}
