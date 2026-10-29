// The pages are split (app/lazyPages.tsx): a test may wait for a page's file before its first findBy*.
import { configure } from "@testing-library/react";

configure({ asyncUtilTimeout: 4000 });
