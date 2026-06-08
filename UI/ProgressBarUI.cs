using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
public class ProgressBarUI : MonoBehaviour
{
    [SerializeField] private GameObject hasProgressGameObject;
    private IHasProgress hasProgress;
    [SerializeField] private Image barImage;
    private void Start()
    {
        hasProgress=hasProgressGameObject.GetComponent<IHasProgress>();
        hasProgress.OnProgressChanged+=HasProgress_OnProgressBarChanged;
        barImage.fillAmount=0f;
        Hide();
    }
    private void HasProgress_OnProgressBarChanged(object sender,IHasProgress.OnProgressChangedEventArgs e)
    {
        barImage.fillAmount=e.progressNormalized;
        if(e.progressNormalized==0f || e.progressNormalized == 1f)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }
    private void Show()
    {
        gameObject.SetActive(true);
        
    }
    private void Hide()
    {
        gameObject.SetActive(false);
    }

}
