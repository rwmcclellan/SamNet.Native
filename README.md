# SamNet.Native

MIT-licensed .NET wrapper around [sam3.cpp](https://github.com/PABannier/sam3.cpp) for high-performance Segment Anything (SAM 2 / SAM 3) inference.

The library runs all native work on a dedicated background thread so the caller’s UI (WPF, Avalonia, WinForms, etc.) stays responsive.

## Architecture Overview

Caller  ──► ConcurrentQueue  ──►  SamWorker (background task)  
│  
├─► sam3.dll (P/Invoke)  
│  
Caller  ◄── UiMessage / SamDetection callbacks  ◄────────┘  

1. Caller enqueues a task (`LoadModel`, `LoadImage`, `Encode`, `Sam2Point`, `Sam3Prompt`, …).
2. The worker thread dequeues and executes the task against the native library.
3. When detections are produced, the worker immediately raises a callback with the mask data.
4. When the task finishes (success or failure), the worker raises a `TaskCompleted` message.

## Supported Tasks

| Command        | Description                                      | Payload                          |
|----------------|--------------------------------------------------|----------------------------------|
| `LoadModel`    | Load a SAM model and create inference state      | `string` (path to model file)   |
| `LoadImage`    | Provide the image to segment                     | `ImageInfo` (RGB byte[], H, W)  |
| `Encode`       | Run the image encoder                            | (uses previously loaded image)  |
| `Sam2Point`    | Point / box prompted segmentation (SAM 2 style)  | `SamMessage`                    |
| `Sam3Prompt`   | Text-prompted concept segmentation (SAM 3)       | `SamMessage` / prompt object    |
| `Quit`         | Shut down the worker and free native resources   | —                               |

## Image Requirements

Format: tightly-packed RGB (3 bytes per pixel)  
Layout: row-major, no padding  
The same image must be loaded + encoded before any segmentation call

## Typical Usage Flow

```csharp
// 1. Create the queue and worker
var queue = new ConcurrentQueue<object>();
var config = new WorkerConfig { DebugLevel = 1 };
var worker = new SamWorker(config, queue);

// 2. Enqueue work
queue.Enqueue(new MessageToWorker(MessageEnum.Task, "LoadModel", @"C:\models\sam3-q4.gguf"));
queue.Enqueue(new MessageToWorker(MessageEnum.Task, "LoadImage", new ImageInfo(rgbBytes, height, width)));
queue.Enqueue(new MessageToWorker(MessageEnum.Task, "Encode"));
queue.Enqueue(new MessageToWorker(MessageEnum.Task, "Sam2Point", samMessage));
// or
queue.Enqueue(new MessageToWorker(MessageEnum.Task, "Sam3Prompt", promptMessage));

// 3. When finished
queue.Enqueue(new MessageToWorker(MessageEnum.Quit));
```

## Callbacks

To register a callback

```csharp
 Action<UiMessage> UiMessageCallBack = new Action<UiMessage>(UiCallback);
```

WPF Implementation - uses dispatcher to handle thread switch to UI

```csharp
private void UiCallback (UiMessage uimess)
{
    Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
    {
        if (uimess.Code == UiEnum.StatusMessage) {}
        else if (uimess.Code == UiEnum.Exception)
        {
             if (uimess.Ob is Exception) {}
        }
        else if (uimess.Code == UiEnum.ReadyToQuit) {}
        else if (uimess.Code == UiEnum.TaskCompleted)
        {                    
            if (uimess.Message.Equals("LoadModel"))
            {  
                if (uimess.Ob is SamResult) {}                       
            }
            else if (uimess.Message.Equals("LoadImage")) 
			{
		        if (uimess.Ob is SamResult) {} 
			}
            else if (uimess.Message.Equals("Encode"))
			{
		        if (uimess.Ob is SamResult) {} 
			}
             else if (uimess.Message.Equals("Sam2Point"))
            {
                if (uimess.Ob is SamResult) {} 
            }
            else if (uimess.Message.Equals("Sam3Prompt"))
            {
                if (uimess.Ob is SamResult) {}
            }
        }
        else if (uimess.Code == UiEnum.SamDetection)
        {
            if (uimess.Ob is SamDetection) {}
        }
    }));
}
  ```         

## Models

All models are available in GGML format on PABannier's Hugging Face repository   
Refer to "Model Zoo" section of About at [sam3.cpp](https://github.com/PABannier/sam3.cpp) for link  
52 model files covering 4 architectures x multiple sizes x up to 5 precisions.

Only about 10% of the models were tested and some are not a fit for this class library

A few of the tested models:  
sam2.1_hiera_tiny_f32.ggml (Sam2.1 - Encode process takes about 6 seconds on an I5 laptop with no acceleration)  
sam3-f16.ggml (Sam3 - Encode process takes about 90 seconds on an I5 laptop with no acceleration)

## Detection Results
Each SamDetection delivered via the callback contains:

MaskData – byte[] of length Width * Height (0/255 binary mask)  
Width / Height  
Score and IoU (when available)  
Optional bounding box

## License
MIT – see LICENSE

## Usage Example

- [SamNet](https://github.com/rwmcclellan/SamNet) - WPF (GNU General Public License v3.0 License)

## Next Steps
Make video interface available.  No short term plan for completion  
Video requires each frame to be encoded which takes the longest time for all tasks to complete.   
Will require time to design and test satisfactorily.  

## Acknowledgements

[sam3.cpp](https://github.com/pabannier/sam3.cpp)  by PABannier   
Meta AI for the original Segment Anything models  
AI support from Grok, Perplexity and ChatGPT
