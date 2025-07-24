using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class SortingVisualizer : MonoBehaviour
{
    [Header("Visualization Settings")]
    public int arraySize = 20;
    public float cubeSpacing = 1.2f;
    public float maxCubeHeight = 10f;
    public float cubeWidth = 1f;

    [Header("Material References")]
    public Material defaultMat;
    public Material comparingMat;
    public Material swappingMat;
    public Material sortedMat;
    public Material pivotMat; // Quick sort pivot material

    [Header("UI References")]
    public Button startButton;
    public Button resetButton;
    public Button exitButton; // Exit button
    public Dropdown algorithmDropdown;
    public Slider speedSlider;
    public TMP_Text statusText;

    [Header("Exit Settings")]
    public bool exitToMainMenu = false;
    public string mainMenuSceneName = "MainMenu";

    private GameObject[] cubes;
    private int[] array;
    private bool isSorting = false;
    private bool isPaused = false;
    private float delay = 0.5f;
    private Coroutine currentSortCoroutine;

    void Start()
    {
        Debug.Log("===== Sorting Visualization Initialization Started =====");

        // Check necessary references
        CheckReferences();

        // Event binding
        startButton.onClick.RemoveAllListeners();
        resetButton.onClick.RemoveAllListeners();
        exitButton.onClick.RemoveAllListeners();
        speedSlider.onValueChanged.RemoveAllListeners();

        startButton.onClick.AddListener(ToggleSorting);
        resetButton.onClick.AddListener(ResetVisualization);
        exitButton.onClick.AddListener(HandleExit);
        speedSlider.onValueChanged.AddListener(UpdateSpeed);

        // Initialize dropdown menu
        InitializeDropdown();

        // Initialize array and cubes
        InitializeArray();
        CreateCubes();

        if (statusText != null)
            statusText.text = "Ready";

        Debug.Log("===== Initialization Completed =====");
    }

    // Check all necessary references
    void CheckReferences()
    {
        if (startButton == null) Debug.LogError("Please assign startButton!");
        if (resetButton == null) Debug.LogError("Please assign resetButton!");
        if (exitButton == null) Debug.LogError("Please assign exitButton!");
        if (algorithmDropdown == null) Debug.LogError("Please assign algorithmDropdown!");
        if (speedSlider == null) Debug.LogError("Please assign speedSlider!");
        if (statusText == null) Debug.LogError("Please assign statusText!");
        if (defaultMat == null) Debug.LogError("Please assign defaultMat!");
        if (comparingMat == null) Debug.LogError("Please assign comparingMat!");
        if (swappingMat == null) Debug.LogError("Please assign swappingMat!");
        if (sortedMat == null) Debug.LogError("Please assign sortedMat!");
    }

    // Exit handling function
    void HandleExit()
    {
        Debug.Log("Exit button clicked");

        // Stop all sorting coroutines
        if (currentSortCoroutine != null)
        {
            StopCoroutine(currentSortCoroutine);
        }

        // Exit application or return to main menu
        if (exitToMainMenu)
        {
            if (!string.IsNullOrEmpty(mainMenuSceneName))
            {
                SceneManager.LoadScene(mainMenuSceneName);
            }
            else
            {
                Debug.LogError("Main menu scene name not set!");
            }
        }
        else
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    // Initialize dropdown menu
    void InitializeDropdown()
    {
        if (algorithmDropdown == null) return;

        algorithmDropdown.ClearOptions();
        algorithmDropdown.options.Add(new Dropdown.OptionData("Bubble Sort"));
        algorithmDropdown.options.Add(new Dropdown.OptionData("Insertion Sort"));
        algorithmDropdown.options.Add(new Dropdown.OptionData("Selection Sort"));
        algorithmDropdown.options.Add(new Dropdown.OptionData("Quick Sort"));
        algorithmDropdown.options.Add(new Dropdown.OptionData("Merge Sort"));
        algorithmDropdown.value = 0;
    }

    // Initialize array
    void InitializeArray()
    {
        array = new int[arraySize];
        for (int i = 0; i < arraySize; i++)
        {
            array[i] = Random.Range(10, 100);
        }
        Debug.Log("Array initialization completed");
    }

    // Create cubes
    void CreateCubes()
    {
        // Clean up old cubes
        if (cubes != null)
        {
            foreach (GameObject cube in cubes)
            {
                if (cube != null) Destroy(cube);
            }
        }

        cubes = new GameObject[arraySize];
        float totalWidth = (arraySize - 1) * cubeSpacing;
        float startX = -totalWidth / 2f;

        Debug.Log($"Creating {arraySize} cubes");

        for (int i = 0; i < arraySize; i++)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            float height = array[i] * maxCubeHeight / 100f;

            cube.transform.position = new Vector3(
                startX + i * cubeSpacing,
                height / 2f,
                0
            );

            cube.transform.localScale = new Vector3(
                cubeWidth,
                height,
                cubeWidth
            );

            if (defaultMat != null)
                cube.GetComponent<Renderer>().material = defaultMat;

            cube.name = $"Cube_{i}_{array[i]}";
            cube.transform.parent = transform;
            cubes[i] = cube;
        }
    }

    // Toggle sorting state (start/pause/resume)
    void ToggleSorting()
    {
        TMP_Text startButtonText = startButton?.GetComponentInChildren<TMP_Text>();
        if (startButtonText == null)
        {
            Debug.LogError("Cannot find TMP_Text component under start button!");
            return;
        }

        if (isSorting)
        {
            isPaused = !isPaused;
            startButtonText.text = isPaused ? "Resume Sorting" : "Pause Sorting";
            if (statusText != null)
                statusText.text = isPaused ? "Paused" : "Sorting...";
        }
        else
        {
            isSorting = true;
            isPaused = false;
            startButtonText.text = "Pause Sorting";
            if (statusText != null)
                statusText.text = "Sorting...";
            currentSortCoroutine = StartCoroutine(StartSorting());
        }
    }

    // Reset visualization
    void ResetVisualization()
    {
        Debug.Log("Resetting visualization");

        if (currentSortCoroutine != null)
        {
            StopCoroutine(currentSortCoroutine);
            currentSortCoroutine = null;
        }

        isSorting = false;
        isPaused = false;

        TMP_Text startButtonText = startButton?.GetComponentInChildren<TMP_Text>();
        if (startButtonText != null)
            startButtonText.text = "Start Sorting";

        if (statusText != null)
            statusText.text = "Ready";

        InitializeArray();
        CreateCubes();
    }

    // Update sorting speed
    void UpdateSpeed(float value)
    {
        delay = Mathf.Lerp(0.5f, 0.01f, value);
        if (statusText != null && !isSorting)
            statusText.text = $"Speed: {value * 100:F0}%";
    }

    // Start sorting coroutine
    IEnumerator StartSorting()
    {
        // Reset all cube materials
        foreach (GameObject cube in cubes)
        {
            if (cube != null)
                cube.GetComponent<Renderer>().material = defaultMat;
        }

        switch (algorithmDropdown.value)
        {
            case 0: // Bubble Sort
                yield return StartCoroutine(BubbleSort());
                break;
            case 1: // Insertion Sort
                yield return StartCoroutine(InsertionSort());
                break;
            case 2: // Selection Sort
                yield return StartCoroutine(SelectionSort());
                break;
            case 3: // Quick Sort
                yield return StartCoroutine(QuickSort(0, arraySize - 1));
                // Mark all elements as sorted
                for (int i = 0; i < arraySize; i++)
                    SetCubeMaterial(i, sortedMat);
                break;
            case 4: // Merge Sort
                yield return StartCoroutine(MergeSort(0, arraySize - 1));
                // Mark all elements as sorted
                for (int i = 0; i < arraySize; i++)
                    SetCubeMaterial(i, sortedMat);
                break;
            default:
                Debug.LogWarning("No sorting algorithm selected");
                break;
        }

        isSorting = false;

        TMP_Text startButtonText = startButton?.GetComponentInChildren<TMP_Text>();
        if (startButtonText != null)
            startButtonText.text = "Start Sorting";

        if (statusText != null)
            statusText.text = "Sorting Completed";
    }

    // Selection Sort
    IEnumerator SelectionSort()
    {
        Debug.Log("Selection Sort started");

        for (int i = 0; i < arraySize - 1; i++)
        {
            int minIndex = i;
            SetCubeMaterial(minIndex, comparingMat);
            yield return WaitForDelay();

            for (int j = i + 1; j < arraySize; j++)
            {
                SetCubeMaterial(j, comparingMat);
                yield return WaitForDelay();

                if (array[j] < array[minIndex])
                {
                    SetCubeMaterial(minIndex, defaultMat);
                    minIndex = j;
                    SetCubeMaterial(minIndex, swappingMat);
                }
                else
                {
                    SetCubeMaterial(j, defaultMat);
                }
                yield return WaitForDelay();
            }

            if (minIndex != i)
            {
                SetCubeMaterial(i, swappingMat);
                yield return WaitForDelay();

                SwapElements(i, minIndex);
                yield return WaitForDelay();
            }

            SetCubeMaterial(i, sortedMat);
            if (minIndex != i)
                SetCubeMaterial(minIndex, defaultMat);
        }

        SetCubeMaterial(arraySize - 1, sortedMat);
        Debug.Log("Selection Sort completed");
    }

    // Quick Sort
    IEnumerator QuickSort(int low, int high)
    {
        if (low < high)
        {
            // Fix material type mismatch: use comparingMat as fallback
            SetCubeMaterial(high, pivotMat != null ? pivotMat : comparingMat);
            yield return WaitForDelay();

            int pivot = array[high];
            int i = low - 1;

            for (int j = low; j < high; j++)
            {
                SetCubeMaterial(j, comparingMat);
                yield return WaitForDelay();

                if (array[j] <= pivot)
                {
                    i++;
                    if (i != j)
                    {
                        SetCubeMaterial(i, swappingMat);
                        yield return WaitForDelay();

                        SwapElements(i, j);
                        yield return WaitForDelay();

                        SetCubeMaterial(i, defaultMat);
                    }
                }
                SetCubeMaterial(j, defaultMat);
            }

            // Place pivot in correct position
            int pivotPosition = i + 1;
            if (pivotPosition != high)
            {
                SwapElements(pivotPosition, high);
                yield return WaitForDelay();
            }

            // Mark pivot as sorted
            SetCubeMaterial(pivotPosition, sortedMat);
            yield return WaitForDelay();

            // Recursively sort left and right partitions
            yield return StartCoroutine(QuickSort(low, pivotPosition - 1));
            yield return StartCoroutine(QuickSort(pivotPosition + 1, high));
        }
        else if (low == high)
        {
            SetCubeMaterial(low, sortedMat);
            yield return WaitForDelay();
        }
    }

    // Merge Sort
    IEnumerator MergeSort(int left, int right)
    {
        if (left < right)
        {
            int mid = (left + right) / 2;

            // Recursively sort left half
            yield return StartCoroutine(MergeSort(left, mid));
            // Recursively sort right half
            yield return StartCoroutine(MergeSort(mid + 1, right));

            // Merge the two halves
            yield return StartCoroutine(Merge(left, mid, right));
        }
        else if (left == right)
        {
            SetCubeMaterial(left, comparingMat);
            yield return WaitForDelay();
            SetCubeMaterial(left, defaultMat);
        }
    }

    // Merge operation for Merge Sort
    IEnumerator Merge(int left, int mid, int right)
    {
        int n1 = mid - left + 1;
        int n2 = right - mid;

        int[] leftArray = new int[n1];
        int[] rightArray = new int[n2];
        int i, j;

        // Copy data to temporary arrays
        for (i = 0; i < n1; i++)
        {
            leftArray[i] = array[left + i];
            SetCubeMaterial(left + i, comparingMat);
        }
        for (j = 0; j < n2; j++)
        {
            rightArray[j] = array[mid + 1 + j];
            SetCubeMaterial(mid + 1 + j, comparingMat);
        }
        yield return WaitForDelay();

        // Merge temporary arrays
        i = 0;
        j = 0;
        int k = left;

        while (i < n1 && j < n2)
        {
            if (leftArray[i] <= rightArray[j])
            {
                array[k] = leftArray[i];
                SetCubeMaterial(k, swappingMat);
                UpdateCubeHeight(k);
                i++;
            }
            else
            {
                array[k] = rightArray[j];
                SetCubeMaterial(k, swappingMat);
                UpdateCubeHeight(k);
                j++;
            }
            yield return WaitForDelay();
            SetCubeMaterial(k, defaultMat);
            k++;
        }

        // Copy remaining elements of leftArray
        while (i < n1)
        {
            array[k] = leftArray[i];
            SetCubeMaterial(k, swappingMat);
            UpdateCubeHeight(k);
            yield return WaitForDelay();
            SetCubeMaterial(k, defaultMat);
            i++;
            k++;
        }

        // Copy remaining elements of rightArray
        while (j < n2)
        {
            array[k] = rightArray[j];
            SetCubeMaterial(k, swappingMat);
            UpdateCubeHeight(k);
            yield return WaitForDelay();
            SetCubeMaterial(k, defaultMat);
            j++;
            k++;
        }
    }

    // Bubble Sort
    IEnumerator BubbleSort()
    {
        Debug.Log("Bubble Sort started");

        for (int i = 0; i < arraySize - 1; i++)
        {
            bool swapped = false;
            for (int j = 0; j < arraySize - i - 1; j++)
            {
                if (j < 0 || j >= cubes.Length || j + 1 >= cubes.Length)
                    continue;

                SetCubeMaterial(j, comparingMat);
                SetCubeMaterial(j + 1, comparingMat);

                yield return WaitForDelay();

                if (array[j] > array[j + 1])
                {
                    SetCubeMaterial(j, swappingMat);
                    SetCubeMaterial(j + 1, swappingMat);

                    yield return WaitForDelay();

                    SwapElements(j, j + 1);
                    swapped = true;

                    yield return WaitForDelay();
                }

                SetCubeMaterial(j, defaultMat);
                SetCubeMaterial(j + 1, defaultMat);
            }

            SetCubeMaterial(arraySize - i - 1, sortedMat);

            if (!swapped)
                break; // No swaps, array is sorted
        }

        // Mark all elements as sorted
        for (int i = 0; i < arraySize; i++)
        {
            SetCubeMaterial(i, sortedMat);
        }
        Debug.Log("Bubble Sort completed");
    }

    // Insertion Sort
    IEnumerator InsertionSort()
    {
        Debug.Log("Insertion Sort started");

        for (int i = 1; i < arraySize; i++)
        {
            int key = array[i];
            int j = i - 1;

            SetCubeMaterial(i, comparingMat);
            yield return WaitForDelay();

            while (j >= 0 && array[j] > key)
            {
                if (j < 0 || j + 1 >= arraySize)
                    break;

                SetCubeMaterial(j, swappingMat);
                SetCubeMaterial(j + 1, swappingMat);

                yield return WaitForDelay();

                array[j + 1] = array[j];
                UpdateCubeHeight(j + 1);

                SetCubeMaterial(j, defaultMat);
                if (j + 1 != i)
                    SetCubeMaterial(j + 1, comparingMat);

                j--;

                yield return WaitForDelay();
            }

            array[j + 1] = key;
            UpdateCubeHeight(j + 1);

            // Mark sorted elements
            for (int k = 0; k <= i; k++)
            {
                SetCubeMaterial(k, sortedMat);
            }
        }

        Debug.Log("Insertion Sort completed");
    }

    // Helper method: swap elements
    void SwapElements(int indexA, int indexB)
    {
        if (indexA < 0 || indexA >= arraySize || indexB < 0 || indexB >= arraySize)
        {
            Debug.LogWarning("Invalid swap indices");
            return;
        }

        // Swap array values
        int temp = array[indexA];
        array[indexA] = array[indexB];
        array[indexB] = temp;

        // Update cube heights
        UpdateCubeHeight(indexA);
        UpdateCubeHeight(indexB);
    }

    // Helper method: update cube height
    void UpdateCubeHeight(int index)
    {
        if (index < 0 || index >= cubes.Length || cubes[index] == null)
            return;

        float height = array[index] * maxCubeHeight / 100f;
        Vector3 position = cubes[index].transform.position;

        cubes[index].transform.position = new Vector3(
            position.x,
            height / 2f,
            position.z
        );

        cubes[index].transform.localScale = new Vector3(
            cubeWidth,
            height,
            cubeWidth
        );
    }

    // Helper method: set cube material
    void SetCubeMaterial(int index, Material mat)
    {
        if (index >= 0 && index < cubes.Length && cubes[index] != null && mat != null)
        {
            cubes[index].GetComponent<Renderer>().material = mat;
        }
    }

    // Helper method: delay with pause support
    IEnumerator WaitForDelay()
    {
        yield return new WaitForSeconds(delay);
        while (isPaused)
        {
            yield return null;
        }
    }
}