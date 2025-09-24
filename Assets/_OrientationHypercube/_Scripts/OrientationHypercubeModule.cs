using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using Rnd = UnityEngine.Random;

public class OrientationHypercubeModule : MonoBehaviour {

    private readonly Dictionary<string, string> _buttonToRotation = new Dictionary<string, string> {
        {"LEFT", "XY"},
        {"RIGHT", "YX"},
        {"IN", "WY"},
        {"OUT", "YW"},
        {"CLOCK", "ZX"},
        {"COUNTER", "XZ"},
    };
    private readonly Dictionary<string, string> _panelButtonDirections = new Dictionary<string, string> {
        { "Right Inner", "+X"},
        { "Left Inner", "-X"},
        { "Top Outer", "+Y"},
        { "Bottom Outer", "-Y"},
        { "Top Inner", "+Z"},
        { "Bottom Inner", "-Z"},
        { "Right Outer", "+W"},
        { "Left Outer", "-W"},
    };
    private readonly string[] _eyeDirections = new string[] {
        "front",
        "right",
        "back",
        "left",
    };

    [SerializeField] private Hypercube _hypercube;
    [SerializeField] private HyperStatusLight _statusLight;
    [SerializeField] private KMSelectable _statusLightButton;

    [SerializeField] private KMSelectable[] _rotationButtons;
    [SerializeField] private KMSelectable _setButton;

    [SerializeField] private KMSelectable[] _panel;
    [SerializeField] private KMSelectable _centrePanelButton;
    [SerializeField] private Animator _panelAnimator;
    [SerializeField] private TextMesh _cbText;

    [SerializeField] private Observer _eye;

    private static int _moduleCount = 0;
    private int _moduleId;
    // Souv info.
    private bool _isSolved = false;
    private string _initialEyePosition;

    private KMAudio _audio;
    private KMBombModule _module;
    private ReadGenerator _readGenerator;

    private string _axes = "XZYW";
    private int _eyeRotation;
    private int[] _signs = new int[] { 1, 1, -1, 1 };
    private List<string> _inputtedRotations = new List<string>();

    private string _highlightedFace = string.Empty;

    private bool _isPreviewMode = false;
    private bool _isRecovery = false;
    private bool _isBusy = false;
    private bool _isMuted = false;
    private bool _cbModeActive = false;

    private void Awake() {
        _moduleId = ++_moduleCount;

        _audio = GetComponent<KMAudio>();
        _module = GetComponent<KMBombModule>();
        _readGenerator = new ReadGenerator(this);

        _cbModeActive = GetComponent<KMColorblindMode>().ColorblindModeActive;

        AssignInteractionHandlers();
    }

    private void AssignInteractionHandlers() {
        foreach (KMSelectable button in _rotationButtons) {
            button.OnInteract += delegate () { HandleRotationPress(button.transform.name.ToUpper()); return false; };
            button.OnInteractEnded += delegate () { PlaySound("Button Release"); };
        }
        _setButton.OnInteract += delegate () { StartCoroutine(HandleSetPress()); return false; };
        _setButton.OnInteractEnded += delegate () { PlaySound("Button Release"); };

        foreach (KMSelectable panelButton in _panel) {
            panelButton.OnHighlight += delegate () { HandleHover(panelButton); };
            panelButton.OnHighlightEnded += delegate () { HandleUnhover(panelButton); };
        }
        _centrePanelButton.OnInteract += delegate () { StartCoroutine(HandleCentrePress()); return false; };
        _statusLightButton.OnInteract += delegate () { PlaySound("Rotation"); _cbModeActive = !_cbModeActive; return false; };
    }

    private void Start() {
        _readGenerator.Generate();
        _hypercube.SetColours(_readGenerator.GetFaceColours());

        _eyeRotation = 0;
        int rotations = Rnd.Range(0, 4);
        for (int i = 0, j = rotations; i < j; i++) {
            ShiftPerspectiveRight();
        }
        _initialEyePosition = _eyeDirections[_eyeRotation];
        Log("-=-=-=- Start -=-=-=-");
        Log($"The observer starts off facing the {_initialEyePosition} face.");
    }

    private void HandleRotationPress(string buttonName) {
        PlaySound("Button Press");
        if (_isBusy || _isRecovery || _isSolved) {
            return;
        }

        if (_isPreviewMode) {
            _hypercube.QueueRotation(GetRotationDigits(_buttonToRotation[buttonName]));
        }
        else {
            Log($"Pressed {buttonName.ToLower()}.");
            _inputtedRotations.Add(GetRotationDigits(_buttonToRotation[buttonName]));
            if (buttonName != "LEFT" && buttonName != "RIGHT") {
                // The observer can stay, move left, or move right with equal probability.
                int rng = Rnd.Range(0, 3);
                if (rng != 0) {
                    ShiftPerspectiveRight(reverse: rng == 1);
                    Log($"The observer moved to face the {_eyeDirections[_eyeRotation]}.");
                }
            }
        }
    }

    private IEnumerator HandleSetPress() {
        PlaySound("Button Press");

        if (_isBusy || _isSolved) {
            yield break;
        }
        if (_isRecovery) {
            EndRecovery();
        }
        else if (!_isPreviewMode) {
            _isBusy = true;
            Log("-=-=-=- Submit -=-=-=-");
            PlaySound("Start Submission");
            yield return StartCoroutine(_hypercube.DisplayFromFaces(_readGenerator.FromFaces));
            _inputtedRotations.ForEach(r => _hypercube.QueueRotation(r));
            _inputtedRotations.Clear();

            while (_hypercube.IsBusy) {
                _hypercube.RotationRate += Time.deltaTime * 0.3f;
                yield return null;
            }

            KMAudio.KMAudioRef thinkingSound = _audio.PlaySoundAtTransformWithRef("Thinking", transform);
            yield return new WaitForSeconds(3);
            thinkingSound.StopSound();

            if (CorrectOrientation()) {
                StartCoroutine(SolveAnimation());
            }
            else {
                StartCoroutine(Strike("The faces did not get mapped to the correct places! Strike!"));
            }
        }
    }

    private void HandleHover(KMSelectable panelButton) {
        panelButton.GetComponent<MeshRenderer>().material.color = Color.white;
        PlaySound("Hover");
        if (panelButton.transform.name != "Centre") {
            _highlightedFace = _panelButtonDirections[panelButton.transform.name];
        }

        if (_isBusy || _isSolved || _isPreviewMode) {
            return;
        }

        if (panelButton.transform.name != "Centre") {
            Highlight();
        }
    }

    private void HandleUnhover(KMSelectable panelButton) {
        panelButton.GetComponent<MeshRenderer>().material.color = Color.white * (49f / 255f);
        _highlightedFace = string.Empty;

        if (_isBusy || _isSolved || _isPreviewMode) {
            return;
        }

        if (panelButton.transform.name != "Centre") {
            Unhighlight();
        }
    }

    private void Highlight() {
        string direction;
        _hypercube.HighlightFace(_highlightedFace, out direction);
        if (_cbModeActive) {
            if (!_isRecovery) {
                _cbText.text = _readGenerator.GetCbText(_highlightedFace);
            }
            else {
                _cbText.text = GetRecoveryCbText(direction);
            }
        }
    }

    private string GetRecoveryCbText(string face) {
        if (_readGenerator.FromFaces.Contains(face)) {
            return "RGB"[Array.IndexOf(_readGenerator.FromFaces, face)].ToString();
        }
        return "K";
    }

    private void Unhighlight() {
        _cbText.text = string.Empty;
        _hypercube.EndHighlight();
    }

    private void EndRecovery() {
        _hypercube.ResetInitialFaceDirections();
        _hypercube.UpdateColours();
        Unhighlight();
        RehighlightFace();
        _isRecovery = false;
    }

    private IEnumerator HandleCentrePress() {
        if (_isBusy || _isSolved) {
            yield break;
        }
        if (_inputtedRotations.Count() > 0) {
            PlaySound("Cannot Change Mode");
            yield break;
        }

        if (_isRecovery) {
            EndRecovery();
        }

        _isBusy = true;
        _hypercube.UpdateColours();
        _hypercube.StopRotations();
        yield return new WaitUntil(() => !_hypercube.IsBusy);
        StartCoroutine(ModeChangeAnimation(!_isPreviewMode));
    }

    private void RehighlightFace() {
        if (_highlightedFace.Length != 0) {
            Highlight();
        }
    }

    private string GetRotationDigits(string rotationLetters) {
        string axesToUse = _isPreviewMode ? "XYZW" : _axes;
        int[] signsToUse = _isPreviewMode ? new int[] { 1, 1, 1, 1 } : _signs;

        int fromDigit = axesToUse.IndexOf(rotationLetters[0]);
        int toDigit = axesToUse.IndexOf(rotationLetters[1]);

        if (signsToUse[fromDigit] != signsToUse[toDigit]) {
            return toDigit.ToString() + fromDigit.ToString();
        }
        return fromDigit.ToString() + toDigit.ToString();
    }

    private void ShiftPerspectiveRight(bool reverse = false) {
        int xPos = _axes.IndexOf('X');
        int yPos = _axes.IndexOf('Y');
        int tempSign = _signs[xPos];

        _axes = _axes.Replace('X', 'A').Replace('Y', 'X').Replace('A', 'Y');

        if (reverse != (_signs[yPos] == tempSign)) {
            _signs[xPos] = _signs[yPos];
            _signs[yPos] = -tempSign;
        }
        else {
            _signs[xPos] = -_signs[yPos];
            _signs[yPos] = tempSign;
        }

        _eyeRotation += 4 + (reverse ? -1 : 1);
        _eyeRotation %= 4;
        _eye.MoveRight(reverse);
    }

    public void Log(string message) {
        Debug.Log($"[Orientation Hypercube #{_moduleId}] {message}");
    }

    public IEnumerator Strike(string strikeMessage) {
        float elapsedTime = 0;
        float animationTime = 0.5f;

        _isBusy = true;
        _statusLight.StrikeFlash();
        PlaySound("Strike");
        _module.HandleStrike();

        yield return null;
        while (elapsedTime < animationTime) {
            elapsedTime += Time.deltaTime;
            _hypercube.WobbleFactor = Hypercube.BASE_WOBBLE_FACTOR * 510 * Mathf.Sin(elapsedTime / animationTime * Mathf.PI);
            yield return null;
        }
        _hypercube.WobbleFactor = Hypercube.BASE_WOBBLE_FACTOR;

        Log($"✕ {strikeMessage}");
        Log("-=-=-=- Reset -=-=-=-");
        _hypercube.RotationRate = 1;
        _isRecovery = true;
        _isBusy = false;
        RehighlightFace();
    }

    public void PlaySound(string soundName) {
        if (!_isMuted) {
            _audio.PlaySoundAtTransform(soundName, transform);
        }
    }

    private IEnumerator ModeChangeAnimation(bool setToPreviewMode) {
        _isBusy = true;
        _panelAnimator.SetTrigger("ModeChange");
        PlaySound("Mode Change");

        float elapsedTime = 0;
        float animationTime = 1;

        yield return null;
        while (elapsedTime < animationTime * 0.5f) {
            elapsedTime += Time.deltaTime;
            float offset = -Mathf.Sin(elapsedTime * Mathf.PI / animationTime);
            _hypercube.transform.localScale = Vector3.one * (1 + offset);
            _hypercube.transform.localPosition = new Vector3(0, 0.5f * offset, 0);
            yield return null;
        }

        Unhighlight();
        if (setToPreviewMode) {
            _hypercube.HighlightFace("None");
            _eye.ToggleDefuserPerspective(true);
        }
        else {
            _eye.ToggleDefuserPerspective(false);
            RehighlightFace();
        }
        _hypercube.transform.localScale = Vector3.zero;

        while (elapsedTime < animationTime) {
            elapsedTime += Time.deltaTime;
            float offset = -Mathf.Sin(elapsedTime * Mathf.PI / animationTime);
            _hypercube.transform.localScale = Vector3.one * (1 + offset);
            _hypercube.transform.localPosition = new Vector3(0, 0.5f * offset, 0);
            yield return null;
        }

        _hypercube.transform.localScale = Vector3.one;
        _hypercube.transform.localPosition = Vector3.zero;
        _isBusy = false;
        _isPreviewMode = setToPreviewMode;
        _hypercube.ResetInitialFaceDirections();
    }

    private bool CorrectOrientation() {
        Face[] faces = _hypercube.GetFaces();
        string[] fromFaces = _readGenerator.FromFaces;
        string[] expectedToFaces = _readGenerator.ToFaces;
        string[] actualToFaces = new string[3];

        foreach (Face face in faces) {
            if (fromFaces.Contains(face.InitialDirection)) {
                actualToFaces[Array.IndexOf(fromFaces, face.InitialDirection)] = face.CurrentDirection;
            }
        }

        Log("The submitted rotations resulted in the following map:");
        Log($"Red: {fromFaces[0]} to {actualToFaces[0]}.");
        Log($"Green: {fromFaces[1]} to {actualToFaces[1]}.");
        Log($"Blue: {fromFaces[2]} to {actualToFaces[2]}.");
        return actualToFaces.Where((dir, ind) => dir != expectedToFaces[ind]).Count() == 0;
    }

    private IEnumerator SolveAnimation() {
        _isSolved = true;
        _isBusy = true;

        _hypercube.UpdateColours();
        Unhighlight();

        _module.HandlePass();
        _statusLight.SolvedState();
        PlaySound("Solve");
        _isMuted = true;
        Log("Submitted the correct orientation!");
        Log("-=-=-=- Solved -=-=-=-");

        _isBusy = false;

        float elapsedTime = 0;
        float[] rotationSpeeds = new float[3];
        float[] mobileOffsets = new float[3];
        float[] currentRotations = new float[3];

        for (int i = 0; i < 3; i++) {
            rotationSpeeds[i] = Rnd.Range(0.5f, 1.5f);
            mobileOffsets[i] = Rnd.Range(0.1f, 0.5f);
        }

        while (true) {
            elapsedTime += Time.deltaTime;

            // Done like this so that it always begins pointing upwards, ie. no rotation.
            currentRotations[0] = Mathf.Sin((rotationSpeeds[0] + mobileOffsets[0]) * elapsedTime);
            currentRotations[1] = Mathf.Cos((rotationSpeeds[1] + mobileOffsets[1]) * elapsedTime);
            currentRotations[2] = Mathf.Sin((rotationSpeeds[2] + mobileOffsets[2]) * elapsedTime);

            if (_hypercube.transform.localPosition.y < 0.2f) {
                _hypercube.transform.localPosition += Vector3.up * Time.deltaTime * 0.1f; ;
            }
            _hypercube.transform.rotation = Quaternion.FromToRotation(Vector3.up, new Vector3(currentRotations[0], currentRotations[1], currentRotations[2]));

            if (!_hypercube.IsBusy) {
                _hypercube.QueueRotation("03");
                _hypercube.QueueRotation("13");
                _hypercube.QueueRotation("23");
                _hypercube.QueueRotation("30");
                _hypercube.QueueRotation("31");
                _hypercube.QueueRotation("32");
            }
            yield return null;
        }
    }

#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"Use '!{0} toggle' to toggle Rotation Preview Mode on and off | "
                                            + "'!{0} highlight <face>' to highlight that face; chain highlights with spaces, "
                                            + "eg. '!{0} highlight zig front right' | '!{0} press <button>' to press that button; "
                                            + "chain up to five presses with spaces, eg. '!{0} press right counter left set'; "
                                            + "the module will NOT stop processing the command if the observer moves | "
                                            + "'!{0} <colourblind/cb>' to toggle colourblind mode.";
#pragma warning restore 414

    private string[] _cbCommands = new string[] { "CB", "COLOURBLIND", "COLORBLIND" };
    private string[] _buttonNames = new string[] { "LEFT", "RIGHT", "IN", "OUT", "CLOCK", "COUNTER", "SET" };
    private string[] _faceNames = new string[] { "FRONT", "BOTTOM", "LEFT", "ZIG", "RIGHT", "ZAG", "BACK", "TOP" };

    private IEnumerator ProcessTwitchCommand(string command) {
        command = command.Trim().ToUpper();

        if (_cbCommands.Contains(command)) {
            yield return null;
            _statusLightButton.OnInteract();
            yield break;
        }

        if (command == "TOGGLE") {
            yield return null;
            _centrePanelButton.OnInteract();
        }

        string[] commands = command.Split(' ');

        if (commands.Length == 0) {
            yield return "sendtochaterror That is an empty command!";
        }

        if (commands[0] == "PRESS") {
            if (commands.Length == 1) {
                yield return "sendtochaterror No buttons were specified.";
            }
            if (commands.Length > 6) {
                yield return "sendtochaterror Cannot chain more than five button presses at once!";
            }
            if (_isRecovery && commands[1] != "SET") {
                yield return "sendtochaterror {0}, button presses have no effect until after pressing Set again, or toggling Rotation Preview Mode. Command cancelled.";
            }
            for (int i = 1; i < commands.Length; i++) {
                if (!_buttonNames.Contains(commands[i])) {
                    yield return $"sendtochaterror '{commands[i]}' is not a valid button.";
                }
            }
            yield return null;

            for (int i = 1; i < commands.Length; i++) {
                if (commands[i] == "SET") {
                    _setButton.OnInteract();
                    yield return new WaitForSeconds(0.1f);
                    _setButton.OnInteractEnded();

                    if (i + 1 < commands.Length) {
                        yield return "sendtochat Stopped executing command after pressing Set.";
                    }
                    yield break;
                }
                else {
                    _rotationButtons[Array.IndexOf(_buttonNames, commands[i])].OnInteract();
                    yield return new WaitForSeconds(0.1f);
                    _rotationButtons[Array.IndexOf(_buttonNames, commands[i])].OnInteractEnded();
                }
                yield return new WaitForSeconds(0.1f);
            }
        }
        else if (commands[0] == "HIGHLIGHT") {
            if (commands.Length == 1) {
                yield return "sendtochaterror No faces were specified.";
            }
            for (int i = 1; i < commands.Length; i++) {
                if (!_faceNames.Contains(commands[i])) {
                    yield return $"sendtochaterror '{commands[i]}' is not a valid face.";
                }
            }
            yield return null;

            for (int i = 1; i < commands.Length; i++) {
                _panel[Array.IndexOf(_faceNames, commands[i])].OnHighlight();
                yield return new WaitForSeconds(1);
                _panel[Array.IndexOf(_faceNames, commands[i])].OnHighlightEnded();
                yield return "trycancel";
            }
        }
        else {
            yield return "sendtochaterror Invalid command!"; // Probably Mar typed this command
        }
    }

    // Each key points to the current position of the face whose initial position was the key's position.
    private readonly Dictionary<string, string[]> _rotationsToButtonPresses = new Dictionary<string, string[]> {
        { "XZ", new string[] { "RIGHT" } },
        { "ZX", new string[] { "LEFT" } },
        { "YZ", new string[] { "LEFT", "CLOCK", "RIGHT" } },
        { "ZY", new string[] { "LEFT", "COUNTER", "RIGHT"  } },
        { "XY", new string[] { "COUNTER" } },
        { "YX", new string[] { "CLOCK" } },
        { "WZ", new string[] { "OUT" } },
        { "ZW", new string[] { "IN" } },
        { "XW", new string[] { "RIGHT", "IN", "LEFT" } },
        { "WX", new string[] { "RIGHT", "OUT", "LEFT" } },
        { "YW", new string[] { "CLOCK", "LEFT", "OUT", "RIGHT", "COUNTER" } },
        { "WY", new string[] { "CLOCK", "LEFT", "IN", "RIGHT", "COUNTER" } },
    };
    private readonly Dictionary<string, string> _opposites = new Dictionary<string, string> {
        { "LEFT", "RIGHT" },
        { "RIGHT", "LEFT" },
        { "IN", "OUT" },
        { "OUT", "IN" },
        { "CLOCK", "COUNTER" },
        { "COUNTER", "CLOCK" },
    };
    private Dictionary<string, string> _currentFaceMaps = new Dictionary<string, string>();
    private string[] _fromFaces;
    private string[] _toFaces;
    private List<string> _rotationSequence = new List<string>();
    private List<string> _buttonPressSequence = new List<string>();
    private int _offsetRightPressCount = 0;
    private int _preOffsetRightShifts = 0;

    private IEnumerator TwitchHandleForcedSolve() {
        // The solving method is not super efficient in that it does not cancel opposite rotations etc, but it works :)
        // EDIT: NOW IT DOES :D
        yield return WaitWhileBusy();
        yield return null;

        if (_isPreviewMode) {
            _centrePanelButton.OnInteract();
            yield return WaitWhileBusy();
        }

        if (_isRecovery) {
            yield return Press("SET");
        }

        _fromFaces = _readGenerator.FromFaces;
        _toFaces = _readGenerator.ToFaces;
        foreach (KeyValuePair<string, string> pair in _panelButtonDirections) {
            _currentFaceMaps.Add(pair.Value, pair.Value);
        }
        foreach (string rotation in _inputtedRotations) {
            UpdateRotationMapByDigits(rotation);
        }

        GenerateRotationSequence();
        _rotationSequence.ForEach(r => _buttonPressSequence = _buttonPressSequence.Concat(_rotationsToButtonPresses[r]).ToList());

        switch (_eyeRotation) {
            case 0: break;
            case 1: _buttonPressSequence.Insert(0, "RIGHT"); break;
            case 2: _buttonPressSequence.Insert(0, "RIGHT"); _buttonPressSequence.Insert(0, "RIGHT"); break;
            case 3: _buttonPressSequence.Insert(0, "LEFT"); break;
        }
        _offsetRightPressCount = _eyeRotation;

        CancelOppositeDirections();
        CancelTripleRepeats();
        CancelOppositeDirections();
        TrimLeftRightRotationsFromEnd();

        for (int i = 0; i < _buttonPressSequence.Count(); i++) {
            if (_buttonPressSequence[i] != "") {
                yield return Press(_buttonPressSequence[i]);

                // Account for eye movement.
                if ((4 + _eyeRotation - _offsetRightPressCount) % 4 == 1) {
                    if (i + 1 < _buttonPressSequence.Count()) {
                        if (_buttonPressSequence[i + 1] == "LEFT") {
                            i++;
                        }
                        else if (i + 2 < _buttonPressSequence.Count() && _buttonPressSequence[i + 1] == "RIGHT" && _buttonPressSequence[i + 2] == "RIGHT") {
                            yield return Press("LEFT");
                            i += 2;
                        }
                        else {
                            yield return Press("RIGHT");
                        }
                    }
                    else {
                        _preOffsetRightShifts--;
                    }
                }
                else if ((4 + _eyeRotation - _offsetRightPressCount) % 4 == 3) {
                    if (i + 1 < _buttonPressSequence.Count()) {
                        if (_buttonPressSequence[i + 1] == "RIGHT") {
                            i++;
                        }
                        else if (i + 2 < _buttonPressSequence.Count() && _buttonPressSequence[i + 1] == "LEFT" && _buttonPressSequence[i + 2] == "LEFT") {
                            yield return Press("RIGHT");
                            i += 2;
                        }
                        else {
                            yield return Press("LEFT");
                        }
                    }
                    else {
                        _preOffsetRightShifts++;
                    }
                }
                _offsetRightPressCount = _eyeRotation;
            }
        }

        switch (((_offsetRightPressCount + _preOffsetRightShifts) % 4 + 4) % 4) {
            case 0: break;
            case 1: yield return Press("LEFT"); break;
            case 2: yield return Press("LEFT"); yield return Press("LEFT"); break;
            case 3: yield return Press("RIGHT"); break;
        }

        yield return Press("SET");
        yield return WaitWhileBusy();
    }

    private void CancelOppositeDirections() {
        for (int i = 0; i < _buttonPressSequence.Count() - 1; i++) {
            if (_buttonPressSequence[i] == _opposites[_buttonPressSequence[i + 1]]) {
                _buttonPressSequence.RemoveRange(i, 2);
                CancelOppositeDirections();
                break;
            }
        }
    }

    private void CancelTripleRepeats() {
        for (int i = 0; i < _buttonPressSequence.Count() - 2; i++) {
            if (_buttonPressSequence[i] == _buttonPressSequence[i + 1] && _buttonPressSequence[i] == _buttonPressSequence[i + 2]) {
                _buttonPressSequence.Insert(i + 3, _opposites[_buttonPressSequence[i]]);
                _buttonPressSequence.RemoveRange(i, 3);
                CancelTripleRepeats();
                break;
            }
        }
    }

    private void TrimLeftRightRotationsFromEnd() {
        for (int i = 0, count = _buttonPressSequence.Count(); i < count; i++) {
            if (_buttonPressSequence[count - (i + 1)] == "LEFT") {
                _preOffsetRightShifts++;
                _buttonPressSequence.RemoveAt(count - (i + 1));
            }
            else if (_buttonPressSequence[count - (i + 1)] == "RIGHT") {
                _preOffsetRightShifts--;
                _buttonPressSequence.RemoveAt(count - (i + 1));
            }
            else {
                return;
            }
        }
    }

    private void UpdateRotationMap(string rotation) {
        var newFaceMaps = new Dictionary<string, string>();

        foreach (KeyValuePair<string, string> pair in _currentFaceMaps) {
            if (pair.Value[1] == rotation[0]) {
                newFaceMaps.Add(pair.Key, $"{pair.Value[0]}{rotation[1]}");
            }
            else if (pair.Value[1] == rotation[1]) {
                // Flip the sign.
                newFaceMaps.Add(pair.Key, $"{"+-".Replace(pair.Value[0].ToString(), "")}{rotation[0]}");
            }
            else {
                newFaceMaps.Add(pair.Key, pair.Value);
            }
        }
        _currentFaceMaps = new Dictionary<string, string>(newFaceMaps);
    }

    private void UpdateRotationMapByDigits(string rotation) {
        UpdateRotationMap($"{"XYZW"[rotation[0] - '0']}{"XYZW"[rotation[1] - '0']}");
    }

    private void GenerateRotationSequence() {
        string rotation;
        string unsolvedAxes = "XYZW";

        for (int i = 0; i < 3; i++) {
            string currentFace = _currentFaceMaps[_fromFaces[i]];
            string currentTarget = _toFaces[i];

            if (currentTarget != currentFace) {
                // If this is an axis flip.
                if (currentFace[1] == currentTarget[1]) {
                    rotation = $"{currentFace[1]}{unsolvedAxes.Replace($"{currentFace[1]}", "")[0]}";
                    _rotationSequence.Add(rotation);
                    _rotationSequence.Add(rotation);
                    UpdateRotationMap(rotation);
                    UpdateRotationMap(rotation);
                }
                else {
                    rotation = currentFace[0] == currentTarget[0] ? $"{currentFace[1]}{currentTarget[1]}" : $"{currentTarget[1]}{currentFace[1]}";
                    _rotationSequence.Add(rotation);
                    UpdateRotationMap(rotation);
                }
            }
            unsolvedAxes = unsolvedAxes.Replace(currentTarget[1].ToString(), "");
        }

    }

    private IEnumerator Press(string button) {
        KMSelectable buttonToPress = button == "SET" ? _setButton : _rotationButtons[Array.IndexOf(_buttonNames, button)];
        buttonToPress.OnInteract();
        yield return new WaitForSeconds(0.1f);
        buttonToPress.OnInteractEnded();
        yield return new WaitForSeconds(0.1f);
    }

    private IEnumerator WaitWhileBusy() {
        while (_isBusy) {
            yield return true;
        }
    }
}
