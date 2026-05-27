using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
public class Player : MonoBehaviour,IKitchenObjectParent
{
    private KitchenObject kitchenObject;
    [SerializeField] private Transform kitchenObjectTopPoint;
    public static Player Instance{get;private set;}

    public event EventHandler <OnSelectedCounterChangedEventArgs> OnSelectedCounterChanged;

    public class OnSelectedCounterChangedEventArgs: EventArgs   {
        public BaseCounter selectedCounter;
    }

    [SerializeField] private float moveSpeed=7f;
    [SerializeField] private GameInput gameInput;
    [SerializeField] private LayerMask counterLayerMask;
    // Update is called once per frame
    private Vector3 lastInteractDir;
    private BaseCounter selectedCounter;
    private void Awake(){
        Instance=this;
    }
    private void Start(){
        gameInput.OnInteractAction+=GameInput_OnInteractAction;
        gameInput.OnInteractAlternateAction+=GameInput_OnInteractAlternateAction;
    }
    private void GameInput_OnInteractAlternateAction(object sender, System.EventArgs e){

        if (selectedCounter!=null){
            selectedCounter.InteractAlternate(this);
        }
       
    }
    private void GameInput_OnInteractAction(object sender, System.EventArgs e){

        if (selectedCounter!=null){
            selectedCounter.Interact(this);
        }
       
    }
    private void Update()
    {
        HandleMovement();
        HandleInteractions();

    }
    private void HandleInteractions(){
        
        Vector2 inputVector=gameInput.GetMovementVectorNormalized();
        Vector3 moveDir =new Vector3 (inputVector.x,0f,inputVector.y);
        if (moveDir!=Vector3.zero){
            lastInteractDir=moveDir;
        }
        float interactDistance=2f;
       if( Physics.Raycast(transform.position,lastInteractDir,out RaycastHit raycasthit,interactDistance,counterLayerMask)){
        if(raycasthit.transform.TryGetComponent(out BaseCounter baseCounter)){
            //has ClearCounter
            if (baseCounter!=selectedCounter){
                
                SetSelectedCounter(baseCounter);
            }
        }else{
            
            SetSelectedCounter(null);
        }
        
        }else{
    
            SetSelectedCounter(null);
        }

    
    }
    private void HandleMovement(){
        Vector2 inputVector=gameInput.GetMovementVectorNormalized();
        Vector3 moveDir =new Vector3 (inputVector.x,0f,inputVector.y);
        float playerSize=0.7f;
        float playerHeight=1.5f;
        float MoveDistance=moveSpeed*Time.deltaTime;
        bool canMove =! Physics.CapsuleCast(transform.position,transform.position+Vector3.up*playerHeight,playerSize,moveDir,MoveDistance);
        if(!canMove){
           Vector3 moveDirX=new Vector3(moveDir.x,0,0).normalized;
           canMove=moveDir.x!=0 && ! Physics.CapsuleCast(transform.position,transform.position+Vector3.up*playerHeight,playerSize,moveDirX,MoveDistance);
            if(canMove){
                moveDir=moveDirX;

            }else{
                Vector3 moveDirZ=new Vector3(0,0,moveDir.z).normalized;
           canMove=moveDir.z!=0 && ! Physics.CapsuleCast(transform.position,transform.position+Vector3.up*playerHeight,playerSize,moveDirZ,MoveDistance);
                if(canMove){
                    moveDir=moveDirZ;
                }else{

                }
            }
        }
        if(canMove) {
        transform.position +=moveDir*moveSpeed*Time.deltaTime;
        }
        float rotateSpeed=10f;
        transform.forward=Vector3.Slerp(transform.forward,moveDir,Time.deltaTime*rotateSpeed);
    }
    private void SetSelectedCounter(BaseCounter selectedCounter){
        this.selectedCounter= selectedCounter;
        OnSelectedCounterChanged?.Invoke(this, new OnSelectedCounterChangedEventArgs{
                    selectedCounter=selectedCounter
                });

    }
        public Transform GetKitchenObjectFollowTransform(){
        return kitchenObjectTopPoint;
    }
    
    public void SetKitchenObject(KitchenObject kitchenObject){
        this.kitchenObject=kitchenObject;
    }

    public KitchenObject GetKitchenObject(){
        return this.kitchenObject;
    }

    public void ClearKitchenObject(){
        kitchenObject=null;
    }

    public bool HasKitchenObject(){
        return kitchenObject!=null;
    }

}
