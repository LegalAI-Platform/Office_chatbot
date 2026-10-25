// Testing Library waits 1 s by default for findBy*/waitFor. With every test file rendering the whole
// app in parallel (jsdom), the first render alone can take longer than that, so the tests failed by
// time, not by behaviour. 4 s keeps a real failure quick and a slow machine green.
import { configure } from "@testing-library/react";

configure({ asyncUtilTimeout: 4000 });
