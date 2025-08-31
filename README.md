# LiveLogStream

A [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) mod for [Resonite](https://resonite.com/).


## Installation

1. Install the [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader).
2. Place the [LiveLogStream.dll](https://github.com/Dexy/LiveLogStream/releases/latest/download/LiveLogStream.dll) into your `rml_mods` folder.
3. Launch the game.





## Slot Structure

```
UserRoot
└── LiveLogStream
    └── <DynamicReferenceVariable<IValue<string>>
        |   VariableName: User/livelog_stream
        └── Reference: <ValueStream<string>>
```
![LiveLogStreamSlot](img/LiveLogStreamSlot.png)

## Accessing the Log Stream

```
LiveLog
├── DynamicReferenceVariableDriver<IValue<string>>
|   ├── VariableName: "User/livelog_stream"
|   └── Target: → ValueSource on ValueDriver^1 on LiveLog
└── ValueDriver<string>
    └── ValueSource: → LiveLog Stream (ValueStream)
```
![LiveLogSlot](img/LiveLogSlot.png)