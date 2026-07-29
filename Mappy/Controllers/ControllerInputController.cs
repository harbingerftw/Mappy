using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Mappy.Controllers;

public sealed unsafe class ControllerInputController : IDisposable
{
    private const float StickDeadzone = 0.16f;
    private const float CursorSpeed = 700.0f;
    private const float ZoomSpeedMultiplier = 5.0f;
    private const float CursorMargin = 24.0f;

    private readonly Hook<AtkModule.Delegates.HandleInput> handleInputHook;
    private readonly Stopwatch frameTimer = Stopwatch.StartNew();

    private Vector2 leftStick;
    private float rightStickY;
    private Vector2 cursorScreenPosition;
    private bool cursorInitialized;

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

    public void ResetCursor()
    {
        cursorInitialized = false;
        leftStick = Vector2.Zero;
        rightStickY = 0.0f;
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
                StripGameplayInput(inputData);
            }
        }

        var result = handleInputHook.Original(thisPtr, inputData, isPadMouseModeEnabled);

        if (mapWasOpen && mapButtonPressed) {
            System.MapWindow.HandleControllerMapButton();
        }

        if (inputData is not null && (suppressBeforeInput || System.MapWindow.IsControllerMoveMode)) {
            StripGameplayInput(inputData);
        }

        return result;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var elapsedSeconds = Math.Min(frameTimer.Elapsed.TotalSeconds, 0.05);
        frameTimer.Restart();

        if (!System.MapWindow.IsControllerMoveMode) {
            cursorInitialized = false;
            return;
        }

        var mapStart = System.MapWindow.MapDrawOffset;
        var mapSize = System.MapWindow.MapContentSize;
        if (mapSize.X <= CursorMargin * 2.0f || mapSize.Y <= CursorMargin * 2.0f) return;

        var minimum = mapStart + new Vector2(CursorMargin);
        var maximum = mapStart + mapSize - new Vector2(CursorMargin);

        if (!cursorInitialized) {
            cursorScreenPosition = mapStart + mapSize / 2.0f;
            cursorInitialized = true;
        }

        var filteredLeftStick = ApplyDeadzone(leftStick, StickDeadzone);
        if (filteredLeftStick != Vector2.Zero) {
            var nextPosition = cursorScreenPosition
                + filteredLeftStick * CursorSpeed * (float)elapsedSeconds;
            var clampedPosition = Vector2.Clamp(nextPosition, minimum, maximum);
            var overflow = nextPosition - clampedPosition;

            cursorScreenPosition = clampedPosition;

            if (overflow != Vector2.Zero) {
                var scale = Math.Max(MapRenderer.MapRenderer.Scale, 0.05f);
                System.MapRenderer.DrawOffset -= overflow / scale;
            }

            MouseDevice.ScheduleCursorMove(
                (int)MathF.Round(cursorScreenPosition.X),
                (int)MathF.Round(cursorScreenPosition.Y));
        }
        else {
            cursorScreenPosition = Vector2.Clamp(ImGui.GetMousePos(), minimum, maximum);
        }

        var filteredZoom = Math.Abs(rightStickY) <= StickDeadzone
            ? 0.0f
            : Math.Clamp(
                (Math.Abs(rightStickY) - StickDeadzone) / (1.0f - StickDeadzone)
                * Math.Sign(rightStickY),
                -1.0f,
                1.0f);

        if (filteredZoom == 0.0f || System.SystemConfig.ZoomLocked) return;

        if (System.SystemConfig.UseLinearZoom) {
            MapRenderer.MapRenderer.Scale += System.SystemConfig.ZoomSpeed
                * filteredZoom
                * ZoomSpeedMultiplier
                * (float)elapsedSeconds;
        }
        else {
            var zoomBase = Math.Max(0.01f, 1.0f + System.SystemConfig.ZoomSpeed);
            MapRenderer.MapRenderer.Scale *= MathF.Pow(
                zoomBase,
                filteredZoom * ZoomSpeedMultiplier * (float)elapsedSeconds);
        }
    }

    private void CaptureSticks(UIInputData* inputData)
    {
        ref var gamepad = ref inputData->GamepadInputs;

        leftStick = new Vector2(
            Math.Clamp(gamepad.LeftStickX / 99.0f, -1.0f, 1.0f),
            -Math.Clamp(gamepad.LeftStickY / 99.0f, -1.0f, 1.0f));
        rightStickY = Math.Clamp(gamepad.RightStickY / 99.0f, -1.0f, 1.0f);
    }

    private static void StripGameplayInput(UIInputData* inputData)
    {
        var previousFilter = inputData->CurrentGamepadInputsFilter;
        inputData->CurrentGamepadInputsFilter |=
            GamepadInputsFilter.LeftStick | GamepadInputsFilter.RightStick;
        inputData->FilterGamepadInputs();
        inputData->CurrentGamepadInputsFilter = previousFilter;

        ClearSticks(ref inputData->GamepadInputs);
        ClearSticks(ref inputData->GamepadInputs2);
    }

    private static void ClearSticks(ref GamepadInputData gamepad)
    {
        gamepad.LeftStickX = 0;
        gamepad.LeftStickY = 0;
        gamepad.RightStickX = 0;
        gamepad.RightStickY = 0;

        gamepad.LeftStickLeft = 0.0f;
        gamepad.LeftStickRight = 0.0f;
        gamepad.LeftStickUp = 0.0f;
        gamepad.LeftStickDown = 0.0f;
        gamepad.RightStickLeft = 0.0f;
        gamepad.RightStickRight = 0.0f;
        gamepad.RightStickUp = 0.0f;
        gamepad.RightStickDown = 0.0f;
    }

    private static Vector2 ApplyDeadzone(Vector2 value, float deadzone)
    {
        var length = value.Length();
        if (length <= deadzone) return Vector2.Zero;

        var scaledLength = Math.Min(1.0f, (length - deadzone) / (1.0f - deadzone));
        return Vector2.Normalize(value) * scaledLength;
    }
}
