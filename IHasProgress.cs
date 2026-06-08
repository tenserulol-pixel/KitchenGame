using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
public interface IHasProgress
{
    public event EventHandler<OnProgressChangedEventArgs> OnProgressChanged;
    public class OnProgressChangedEventArgs: EventArgs
    {
        public float progressNormalized;
    }

}
