using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Mappy.Controllers;

public sealed unsafe class ControllerInputController : IDisposable
{
    private const float StickDeadzone = 0.16f;
    private const float PanSpeed = 700.0f;
    private const float ZoomSpeedMultiplier = 5.0f;

    private readonly Hook<AtkModule.Delegates.HandleInput> handleInputHook;
    private readonly Stopwatch frameTimer = Stopwatch.StartNew();

    private Vector2 rightStick;
    private bool zoomModifierHeld;

    public ControllerInputController()
    {
        handleInputHook = Service.Hooker.HookFromAddress<AtkModule.Delegates.HandleInput>(
            AtkModule.MemberFunctionPointers.HandleInput,
            OnHandleInput);
        handleInputHook.Enable();

        Service.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Service.Framework.Update -= OnFrameworkUpdate;
        handleInputHook.Dispose();
    }

    public static bool IsMapButtonPressed()
    {
        var inputData = UIInputData.Instance();
        return inputData is not null && inputData->IsInputIdPressed(InputId.PAD_MAP);
    }

    public void ResetInput()
    {
        rightStick = Vector2.Zero;
        zoomModifierHeld = false;
        frameTimer.Restart();
    }

    private byte OnHandleInput(AtkModule* thisPtr, UIInputData* inputData, bool isPadMouseModeEnabled)
    {
        var mapWasOpen = System.MapWindow.IsOpen;
        var suppressBeforeInput = System.MapWindow.IsControllerMoveMode;
        var mapButtonPressed = inputData is not null && inputData->IsInputIdPressed(InputId.PAD_MAP);

        if (inputData is not null) {
            CaptureSticks(inputData);

            if (suppressBeforeInput) {
                StripGameplayInput(inputData, zoomModifierHeld);
            }
        }

        var result = handleInputHook.Original(thisPtr, inputData, isPadMouseModeEnabled);

        if (mapWasOpen && mapButtonPressed) {
            System.MapWindow.HandleControllerMapButton();
        }

        if (inputData is not null && (suppressBeforeInput || System.MapWindow.IsControllerMoveMode)) {
            StripGameplayInput(inputData, zoomModifierHeld);
        }

        return result;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var elapsedSeconds = Math.Min(frameTimer.Elapsed.TotalSeconds, 0.05);
        frameTimer.Restart();

        if (!System.MapWindow.IsControllerMoveMode) {
            rightStick = Vector2.Zero;
            return;
        }

        var filteredRightStick = ApplyDeadzone(rightStick, StickDeadzone);
        if (filteredRightStick == Vector2.Zero) return;

        if (zoomModifierHeld) {
            var zoomInput = -filteredRightStick.Y;
            if (zoomInput == 0.0f || System.SystemConfig.ZoomLocked) return;

            if (System.SystemConfig.UseLinearZoom) {
                MapRenderer.MapRenderer.Scale += System.SystemConfig.ZoomSpeed
                    * zoomInput
                    * ZoomSpeedMultiplier
                    * (float)elapsedSeconds;
            }
            else {
                var zoomBase = Math.Max(0.01f, 1.0f + System.SystemConfig.ZoomSpeed);
                MapRenderer.MapRenderer.Scale *= MathF.Pow(
                    zoomBase,
                    zoomInput * ZoomSpeedMultiplier * (float)elapsedSeconds);
            }

            return;
        }

        var scale = Math.Max(MapRenderer.MapRenderer.Scale, 0.05f);
        System.MapRenderer.DrawOffset -=
            filteredRightStick * PanSpeed * (float)elapsedSeconds / scale;
    }

    private void CaptureSticks(UIInputData* inputData)
    {
        ref var gamepad = ref inputData->GamepadInputs;

        rightStick = new Vector2(
            Math.Clamp(gamepad.RightStickX / 99.0f, -1.0f, 1.0f),
            -Math.Clamp(gamepad.RightStickY / 99.0f, -1.0f, 1.0f));
        zoomModifierHeld =
            inputData->IsInputIdHeld(InputId.VIRTUAL_PAD_L1) ||
            gamepad.Buttons.HasFlag(GamepadButtonsFlags.L1) ||
            inputData->GamepadInputs2.Buttons.HasFlag(GamepadButtonsFlags.L1) ||
            gamepad.L1 > 0.5f ||
            inputData->GamepadInputs2.L1 > 0.5f;
    }

    private static void StripGameplayInput(UIInputData* inputData, bool stripZoomModifier)
    {
        ClearRightStick(ref inputData->GamepadInputs);
        ClearRightStick(ref inputData->GamepadInputs2);

        if (stripZoomModifier) {
            ClearZoomModifier(ref inputData->GamepadInputs);
            ClearZoomModifier(ref inputData->GamepadInputs2);
        }
    }

    private static void ClearRightStick(ref GamepadInputData gamepad)
    {
        gamepad.RightStickX = 0;
        gamepad.RightStickY = 0;

        gamepad.RightStickLeft = 0.0f;
        gamepad.RightStickRight = 0.0f;
        gamepad.RightStickUp = 0.0f;
        gamepad.RightStickDown = 0.0f;
    }

    private static void ClearZoomModifier(ref GamepadInputData gamepad)
    {
        gamepad.Buttons &= ~GamepadButtonsFlags.L1;
        gamepad.ButtonsPressed &= ~GamepadButtonsFlags.L1;
        gamepad.ButtonsReleased &= ~GamepadButtonsFlags.L1;
        gamepad.ButtonsRepeat &= ~GamepadButtonsFlags.L1;
        gamepad.L1 = 0.0f;
    }

    private static Vector2 ApplyDeadzone(Vector2 value, float deadzone)
    {
        var length = value.Length();
        if (length <= deadzone) return Vector2.Zero;

        var scaledLength = Math.Min(1.0f, (length - deadzone) / (1.0f - deadzone));
        return Vector2.Normalize(value) * scaledLength;
    }
}
