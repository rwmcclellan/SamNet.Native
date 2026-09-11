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

## Typical Usage Flow

```csharp
// 1. Create the queue and worker
var queue = new ConcurrentQueue<object>();
var config = new WorkerConfig { DebugLevel = 1 };
var worker = new SamWorker(config, queue);

// 2. Wire up callbacks
worker.SetActionDestinations(
    uiMsg =>
    {
        switch (uiMsg.Code)
        {
            case UiEnum.SamDetection:
                var det = uiMsg.Ob as SamDetection;
                // det.MaskData (byte[]), det.Width, det.Height, det.Score, ...
                break;

            case UiEnum.TaskCompleted:
                // Handle success / failure of the last command
                break;

            case UiEnum.Exception:
            case UiEnum.StatusMessage:
                // Logging / error handling
                break;
        }
    },
    byteMsg => { /* optional raw byte-array messages */ }
);

// 3. Enqueue work
queue.Enqueue(new MessageToWorker(MessageEnum.Task, "LoadModel", @"C:\models\sam3-q4.gguf"));
queue.Enqueue(new MessageToWorker(MessageEnum.Task, "LoadImage", new ImageInfo(rgbBytes, height, width)));
queue.Enqueue(new MessageToWorker(MessageEnum.Task, "Encode"));
queue.Enqueue(new MessageToWorker(MessageEnum.Task, "Sam2Point", samMessage));
// or
queue.Enqueue(new MessageToWorker(MessageEnum.Task, "Sam3Prompt", promptMessage));

// 4. When finished
queue.Enqueue(new MessageToWorker(MessageEnum.Quit));
```

## Image Requirements

Format: tightly-packed RGB (3 bytes per pixel)  
Layout: row-major, no padding  
The same image must be loaded + encoded before any segmentation call

## Models

## Detection Results
Each SamDetection delivered via the callback contains:

MaskData – byte[] of length Width * Height (0/255 binary mask)  
Width / Height  
Score and IoU (when available)  
Optional bounding box

## License
MIT – see LICENSE

## Usage Example
In project SamNet - Available soon

## Next Steps
Make video interface available.  No short term plan for completion  
Video requires each frame to be encoded which takes the longest time for all tasks to complete.   
Will require time to design and test satisfactorily.  

## Acknowledgements

sam3.cpp by PABannier  
Meta AI for the original Segment Anything models  
AI support from Grok, Perplexity and ChatGPT
