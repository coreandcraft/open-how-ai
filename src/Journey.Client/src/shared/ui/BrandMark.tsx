import type { SVGProps } from "react";

/** The openhow Open pages brand mark. */
export function BrandMark({
  size = 24,
  ...props
}: SVGProps<SVGSVGElement> & { size?: number }) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width={size}
      height={size}
      viewBox="0 0 34 34"
      fill="none"
      stroke="currentColor"
      strokeWidth={2.2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      {...props}
    >
      <path d="M16 26V9C12 5 7 4 3 5v18c5-1 9 0 13 3Zm0 0V9c4-4 9-5 13-4v18c-5-1-9 0-13 3Z" />
    </svg>
  );
}
