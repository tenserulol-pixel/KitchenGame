using UnityEngine;
using System.Collections;
using System.Collections.Generic;
public class ContainerCounterVisual : MonoBehaviour
{
    private const string OPEN_CLOSE="OpenClose";
    [SerializeField] private ContainerCounter containerCounter;
    private Animator animator;
    private void Awake(){
        animator = GetComponent<Animator>();
    }
    private void Start(){
        containerCounter.OnPlayerGrabbedObject+=ContainerCounter_OnPlayerGrabbedObject;
        // ★ Возврат ингредиента в контейнер — та же анимация крышки, что и при взятии
        containerCounter.OnPlayerReturnedObject+=ContainerCounter_OnPlayerReturnedObject;

    }
    private void ContainerCounter_OnPlayerGrabbedObject(object sender,System.EventArgs e){
        animator.SetTrigger(OPEN_CLOSE);
    }
    private void ContainerCounter_OnPlayerReturnedObject(object sender,System.EventArgs e){
        animator.SetTrigger(OPEN_CLOSE);
    }
}
