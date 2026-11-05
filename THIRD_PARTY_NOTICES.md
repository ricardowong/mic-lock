# Third-Party Notices & Acknowledgements

MicLock includes architectural patterns and sequencing logic adapted from or inspired by the following open-source software:

---

## Microsoft PowerToys (Keyboard Manager)

Portions of the keyboard hook event lifecycle and modifier state handling (such as atomic SendInput modifier releases, synthetic dummy key event masking to disarm the Windows shell menu, and low-level physical modifier suppression) are adapted from Microsoft PowerToys Keyboard Manager.

- **Project:** [Microsoft PowerToys](https://github.com/microsoft/PowerToys)
- **License:** MIT License

```text
The MIT License (MIT)

Copyright (c) Microsoft Corporation. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
